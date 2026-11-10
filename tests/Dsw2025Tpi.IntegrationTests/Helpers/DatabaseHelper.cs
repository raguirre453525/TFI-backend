using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests.Helpers;

public static class DatabaseHelper
{
    public static string GenerateUniqueSku(string prefix = "TP4")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var sku = $"{prefix}-{suffix}";
        if (sku.Length > 20) sku = sku[..20];
        return sku;
    }

    public static async Task<Product> SeedProductAsync(
        CustomWebApplicationFactory factory,
        string sku,
        string name,
        string internalCode,
        decimal price,
        int stock,
        bool isActive = true)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var product = new Product(sku, internalCode, name, "Descripcion test", price, stock)
        {
            IsActive = isActive
        };
        db.Set<Product>().Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public static async Task<Product> SeedProductAsync(
        CustomWebApplicationFactory factory,
        string sku,
        string name,
        string? internalCode,
        decimal price,
        int stock,
        bool isActive,
        string? description)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var product = new Product(sku, internalCode ?? string.Empty, name, description ?? "Descripcion test", price, stock)
        {
            IsActive = isActive
        };
        db.Set<Product>().Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}
