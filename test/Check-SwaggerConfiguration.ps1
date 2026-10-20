param(
    [Parameter(Mandatory = $true)]
    [string]$MigrationScript,
    [string]$ApiDirectory = (Join-Path $PSScriptRoot '..\Dsw2025Tpi.Api\bin\Debug\net8.0'),
    [string]$LocalDbInstance = '(localdb)\MSSQLLocalDB'
)

$ErrorActionPreference = 'Stop'
$api = (Resolve-Path $ApiDirectory).Path
$dll = Join-Path $api 'Dsw2025Tpi.Api.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the API before running this check.' }
$schema = Get-Content -LiteralPath $MigrationScript -Raw
$database = 'Tp3SwaggerCheck_' + [Guid]::NewGuid().ToString('N')
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) $database
[void][System.IO.Directory]::CreateDirectory($scratch)
$master = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
$master['Data Source'] = $LocalDbInstance
$master['Initial Catalog'] = 'master'
$master['Integrated Security'] = $true
$master['Connect Timeout'] = 15
$testConnection = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $master.ConnectionString
$testConnection['Initial Catalog'] = $database

function Invoke-DatabaseCommand([string]$ConnectionString, [string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection $ConnectionString
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 60
        [void]$command.ExecuteNonQuery()
        $command.Dispose()
    } finally { $connection.Dispose() }
}

function Get-HttpResult([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return @{ Status = [int]$response.StatusCode; Content = $response.Content }
    } catch {
        if ($null -ne $_.Exception.Response) {
            return @{ Status = [int]$_.Exception.Response.StatusCode; Content = '' }
        }
        return @{ Status = 0; Content = '' }
    }
}

$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$key = [Convert]::ToBase64String($bytes)
$cases = @(
    @{ Name = 'Development default'; Environment = 'Development'; Enabled = $null; Expected = 200 },
    @{ Name = 'Production default'; Environment = 'Production'; Enabled = $null; Expected = 404 },
    @{ Name = 'Production enabled'; Environment = 'Production'; Enabled = 'true'; Expected = 200 },
    @{ Name = 'Production disabled'; Environment = 'Production'; Enabled = 'false'; Expected = 404 }
)

$created = $false
try {
    Invoke-DatabaseCommand $master.ConnectionString ('CREATE DATABASE [' + $database + ']')
    $created = $true
    $schemaConnection = New-Object System.Data.SqlClient.SqlConnection $testConnection.ConnectionString
    try {
        $schemaConnection.Open()
        foreach ($batch in [regex]::Split($schema, '(?im)^\s*GO\s*\r?$')) {
            if (-not [string]::IsNullOrWhiteSpace($batch)) {
                $command = $schemaConnection.CreateCommand()
                try {
                    $command.CommandText = $batch
                    $command.CommandTimeout = 60
                    [void]$command.ExecuteNonQuery()
                } finally { $command.Dispose() }
            }
        }
    } finally { $schemaConnection.Dispose() }

    foreach ($case in $cases) {
        $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Loopback), 0
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $listener.Stop()
        $url = 'http://127.0.0.1:' + $port
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = 'dotnet'
        $start.Arguments = '"' + $dll + '"'
        $start.WorkingDirectory = $api
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        # Do not inherit credentials, a real database, or environment overrides.
        foreach ($name in @($start.EnvironmentVariables.Keys)) {
            if ($name -match '^(Jwt|BootstrapAdmin|ConnectionStrings|Swagger|Kestrel)(__|:)' -or
                $name -match '^(ASPNETCORE_|DOTNET_ENVIRONMENT$|DOTNET_LAUNCH_PROFILE$)') {
                $start.EnvironmentVariables.Remove($name)
            }
        }
        $start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = $case.Environment
        $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = $case.Environment
        $start.EnvironmentVariables['ASPNETCORE_URLS'] = $url
        $start.EnvironmentVariables['ConnectionStrings__Dsw2025TpiEntities'] = $testConnection.ConnectionString
        $start.EnvironmentVariables['Jwt__Key'] = $key
        # Development user secrets must not read the developer's real profile.
        $start.EnvironmentVariables['APPDATA'] = $scratch
        if ($null -ne $case.Enabled) {
            $start.EnvironmentVariables['Swagger__Enabled'] = $case.Enabled
        }
        $process = [System.Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try {
            $ready = $false
            for ($attempt = 0; $attempt -lt 60; $attempt++) {
                if ($process.HasExited) { break }
                if ((Get-HttpResult ($url + '/healthcheck')).Status -eq 200) {
                    $ready = $true
                    break
                }
                Start-Sleep -Milliseconds 250
            }
            if (-not $ready) { throw ('API did not become ready: ' + $case.Name) }
            $ui = Get-HttpResult ($url + '/swagger/index.html')
            $document = Get-HttpResult ($url + '/swagger/v1/swagger.json')
            if ($ui.Status -ne $case.Expected -or $document.Status -ne $case.Expected) {
                throw ('Swagger check failed: ' + $case.Name + '; UI=' + $ui.Status + '; OpenAPI=' + $document.Status + '; expected=' + $case.Expected)
            }
            if ($case.Expected -eq 200) {
                $openApi = $document.Content | ConvertFrom-Json
                if ($ui.Content -notmatch 'swagger-ui' -or $openApi.openapi -notlike '3.*' -or
                    @($openApi.paths.PSObject.Properties).Count -eq 0) {
                    throw ('Invalid Swagger UI or document: ' + $case.Name)
                }
            }
            if ((Get-HttpResult ($url + '/api/orders')).Status -ne 401) {
                throw ('Order authentication changed: ' + $case.Name)
            }
            'PASS: ' + $case.Name + ' (UI, OpenAPI, health and unauthorized orders)'
        } finally {
            if (-not $process.HasExited) { $process.Kill() }
            [void]$process.WaitForExit(10000)
            # Drain output without exposing possible application credentials.
            [void]$stdout.Result
            [void]$stderr.Result
            $process.Dispose()
        }
    }
} finally {
    if ($created) {
        [System.Data.SqlClient.SqlConnection]::ClearAllPools()
        Invoke-DatabaseCommand $master.ConnectionString ('ALTER DATABASE [' + $database + '] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [' + $database + ']')
        'CLEANUP: isolated Swagger test database removed.'
    }
    Remove-Item -LiteralPath $scratch -Recurse -Force
}
