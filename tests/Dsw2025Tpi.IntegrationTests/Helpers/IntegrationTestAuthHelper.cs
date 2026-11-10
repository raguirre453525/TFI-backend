using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests.Helpers;

public static class IntegrationTestAuthHelper
{
    public static string GenerateUniqueSku(string prefix = "TP4")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var sku = $"{prefix}-{suffix}";
        if (sku.Length > 20) sku = sku[..20];
        return sku;
    }

    public static async Task<string> GetAdminTokenAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();

        if (!await roleManager.RoleExistsAsync("ADMIN"))
            await roleManager.CreateAsync(new IdentityRole("ADMIN"));
        if (!await roleManager.RoleExistsAsync("USER"))
            await roleManager.CreateAsync(new IdentityRole("USER"));

        var username = $"admin_{Guid.NewGuid():N}";
        var email = $"{username}@test.local";
        var user = new IdentityUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, "Test123!");
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to create ADMIN user: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        var roleResult = await userManager.AddToRoleAsync(user, "ADMIN");
        if (!roleResult.Succeeded)
            throw new InvalidOperationException($"Failed to add ADMIN role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");

        return jwtService.GenerateToken(user.UserName!, "ADMIN");
    }

    public static async Task<(string token, Guid customerId)> GetUserTokenWithCustomerAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var jwtService = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

        if (!await roleManager.RoleExistsAsync("USER"))
            await roleManager.CreateAsync(new IdentityRole("USER"));
        if (!await roleManager.RoleExistsAsync("ADMIN"))
            await roleManager.CreateAsync(new IdentityRole("ADMIN"));

        var username = $"user_{Guid.NewGuid():N}";
        var email = $"{username}@test.local";
        var user = new IdentityUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, "Test123!");
        if (!result.Succeeded)
            throw new InvalidOperationException($"Failed to create USER: {string.Join(", ", result.Errors.Select(e => e.Description))}");

        var roleResult = await userManager.AddToRoleAsync(user, "USER");
        if (!roleResult.Succeeded)
            throw new InvalidOperationException($"Failed to add USER role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");

        var customer = new Customer
        {
            Name = $"TestCust {Guid.NewGuid():N}"[..12],
            Email = email,
            IdentityUserId = user.Id
        };
        db.Set<Customer>().Add(customer);
        await db.SaveChangesAsync();

        var token = jwtService.GenerateToken(user.UserName!, "USER");
        return (token, customer.Id);
    }
}
