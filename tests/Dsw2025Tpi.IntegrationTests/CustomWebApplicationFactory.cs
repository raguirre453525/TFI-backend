using Dsw2025Tpi.Api;
using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // La API exige Jwt:Key por variable de entorno (Program.cs). Como el
        // WebApplicationBuilder de Main no ve el InMemory de ConfigureAppConfiguration,
        // la clave se inyecta por entorno del proceso de test.
        Environment.SetEnvironmentVariable(
            "Jwt__Key", "tp4-testing-key-0123456789abcdef0123");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "Testing");
        Environment.SetEnvironmentVariable("Jwt__Audience", "Testing");

        builder.ConfigureServices(services =>
        {
            // Main ya registro SqlServer cuando corre este callback: purgar ese
            // proveedor antes de registrar Sqlite, si no EF ve dos proveedores.
            foreach (var d in services
                .Where(d => EsServicioEf(d.ServiceType)
                    || EsServicioEf(d.ImplementationType)
                    || EsServicioEf(d.ImplementationInstance?.GetType()))
                .ToList())
            {
                services.Remove(d);
            }

            _connection = new SqliteConnection("DataSource=:memory:;Cache=Shared");
            _connection.Open();

            services.AddDbContext<Dsw2025TpiContext>(options => options.UseSqlite(_connection));

            // Nota: Main ejecuta MigrateAsync() al arrancar; contra SQLite las
            // operaciones scaffolded se traducen al proveedor actual. Si alguna
            // migracion futura trae SQL crudo de SQL Server, este punto fallara
            // y habra que condicionar el Migrate por entorno.
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Close();
        _connection?.Dispose();
    }

    private static bool EsServicioEf(Type? type) =>
        type?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true;
}
