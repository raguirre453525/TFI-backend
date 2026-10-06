param(
    [string]$ApiDirectory = (Join-Path $PSScriptRoot '..\Dsw2025Tpi.Api')
)

$ErrorActionPreference = 'Stop'
$api = (Resolve-Path $ApiDirectory).Path
$dll = Join-Path $api 'bin\Debug\net8.0\Dsw2025Tpi.Api.dll'
if (-not (Test-Path -LiteralPath $dll)) {
    throw 'Build the backend in Debug before running this check.'
}

# Each child uses isolated configuration and an invalid connection string.
# No database connection, user secret, or administrator account is needed.
$bytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
$validKey = [Convert]::ToBase64String($bytes)
$jwtError = 'Configure Jwt:Key with at least 32 UTF-8 bytes'
$adminError = 'Configure BootstrapAdmin:Email, BootstrapAdmin:UserName and BootstrapAdmin:Password together'
$databaseBoundary = "Keyword not supported: 'startupcheck'"
$cases = @(
    @{ Name = 'Missing JWT key'; Key = $null; Expected = $jwtError },
    @{ Name = 'Blank JWT key'; Key = '   '; Expected = $jwtError },
    @{ Name = 'Short JWT key'; Key = 'x' * 31; Expected = $jwtError },
    @{ Name = 'Email alone'; Key = $validKey; Admin = @{ Email = 'startup-check@example.invalid' }; Expected = $adminError },
    @{ Name = 'User name alone'; Key = $validKey; Admin = @{ UserName = 'startup-check' }; Expected = $adminError },
    @{ Name = 'Password alone'; Key = $validKey; Admin = @{ Password = $validKey }; Expected = $adminError },
    @{ Name = 'Blank bootstrap field'; Key = $validKey; Admin = @{ Email = ' '; UserName = 'startup-check'; Password = $validKey }; Expected = $adminError },
    @{ Name = 'No bootstrap credentials'; Key = $validKey; Expected = $databaseBoundary },
    @{ Name = 'Complete bootstrap configuration'; Key = $validKey; Admin = @{ Email = 'startup-check@example.invalid'; UserName = 'startup-check'; Password = $validKey }; Expected = $databaseBoundary }
)

foreach ($case in $cases) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'dotnet'
    $start.Arguments = '"' + $dll + '"'
    $start.WorkingDirectory = $api
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    # Remove inherited configuration so this check cannot use real credentials.
    foreach ($name in @($start.EnvironmentVariables.Keys)) {
        if ($name -match '^(Jwt|BootstrapAdmin|ConnectionStrings)(__|:)') {
            $start.EnvironmentVariables.Remove($name)
        }
    }
    $start.EnvironmentVariables['DOTNET_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['ASPNETCORE_ENVIRONMENT'] = 'Production'
    $start.EnvironmentVariables['ConnectionStrings__Dsw2025TpiEntities'] = 'startupcheck=1'
    if ($null -ne $case.Key) { $start.EnvironmentVariables['Jwt__Key'] = $case.Key }
    if ($case.Admin) {
        foreach ($field in $case.Admin.Keys) {
            $start.EnvironmentVariables['BootstrapAdmin__' + $field] = $case.Admin[$field]
        }
    }

    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) {
            $process.Kill()
            throw ('Startup check timed out: ' + $case.Name)
        }
        $output = $stdout.Result + $stderr.Result
        if ($process.ExitCode -eq 0 -or -not $output.Contains($case.Expected)) {
            # Do not print child output: future failures could contain credentials.
            throw ('Startup check failed: ' + $case.Name)
        }
        'PASS: ' + $case.Name
    } finally {
        $process.Dispose()
    }
}
