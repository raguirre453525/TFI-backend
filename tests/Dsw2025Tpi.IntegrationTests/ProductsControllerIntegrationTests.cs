using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class ProductsControllerIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AddProduct_WhenUnauthenticatedOrNotAdmin_ReturnsUnauthorizedOrForbidden()
    {
        // Arrange - two unique SKUs for isolation
        var skuUnauth = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T1A");
        var skuUser = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T1B");
        var internalCodeUnauth = $"IC-{Guid.NewGuid():N}"[..12];
        var internalCodeUser = $"IC-{Guid.NewGuid():N}"[..12];

        var requestUnauth = new ProductModel.ProductRequest(skuUnauth, internalCodeUnauth, "TP4 T1 Unauth Product", "Desc", 100m, 10);
        var requestUser = new ProductModel.ProductRequest(skuUser, internalCodeUser, "TP4 T1 User Product", "Desc", 100m, 10);

        // Act - unauthenticated
        var clientUnauth = _factory.CreateClient();
        var responseUnauth = await clientUnauth.PostAsJsonAsync("/api/products", requestUnauth);

        // Assert - unauthenticated => 401
        responseUnauth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify no product persisted for unauth SKU
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var persisted = await db.Set<Product>().FirstOrDefaultAsync(p => p.Sku == skuUnauth);
            persisted.Should().BeNull("unauthenticated request must not persist product");
        }

        // Arrange - authenticated as USER
        var (userToken, _) = await IntegrationTestAuthHelper.GetUserTokenWithCustomerAsync(_factory);
        var clientUser = _factory.CreateClient();
        clientUser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        // Act - USER token (not ADMIN) should be forbidden
        var responseUser = await clientUser.PostAsJsonAsync("/api/products", requestUser);

        // Assert - USER => 403 Forbidden
        responseUser.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify no product persisted for USER SKU
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var persisted = await db.Set<Product>().FirstOrDefaultAsync(p => p.Sku == skuUser);
            persisted.Should().BeNull("non-admin USER request must not persist product");
        }
    }

    [Fact]
    public async Task AddProduct_WhenAuthenticatedAsAdmin_PersistsInSqliteAndReturnsCreated()
    {
        // Arrange
        var adminToken = await IntegrationTestAuthHelper.GetAdminTokenAsync(_factory);
        var sku = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T2");
        var internalCode = $"IC-T2-{Guid.NewGuid():N}"[..10];
        var name = $"TP4-T2 Product {Guid.NewGuid():N}"[..20];
        const decimal price = 250.50m;
        const int stock = 15;
        var request = new ProductModel.ProductRequest(sku, internalCode, name, "Test description T2", price, stock);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/products", request);

        // Assert - HTTP 201 Created
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("api/products");

        var body = await response.Content.ReadFromJsonAsync<ProductModel.ProductResponse>();
        body.Should().NotBeNull();
        body!.Sku.Should().Be(sku);
        body.Name.Should().Be(name);
        body.CurrentUnitPrice.Should().Be(price);
        body.StockQuantity.Should().Be(stock);
        body.IsActive.Should().BeTrue();
        body.InternalCode.Should().Be(internalCode);

        // Verify persistence in SQLite via DbContext
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var persisted = await db.Set<Product>().FirstOrDefaultAsync(p => p.Sku == sku);
        persisted.Should().NotBeNull();
        persisted!.Sku.Should().Be(sku);
        persisted.Name.Should().Be(name);
        persisted.CurrentUnitPrice.Should().Be(price);
        persisted.StockQuantity.Should().Be(stock);
        persisted.IsActive.Should().BeTrue();
        persisted.InternalCode.Should().Be(internalCode);
    }

    [Fact]
    public async Task AddProduct_WhenSkuAlreadyExistsInDatabase_ReturnsConflictStatusCode()
    {
        // Arrange - seed product via DbContext with unique SKU
        var sku = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T3");
        var internalCode = $"IC-T3-{Guid.NewGuid():N}"[..10];
        var name = $"TP4-T3 Original {Guid.NewGuid():N}"[..20];

        await DatabaseHelper.SeedProductAsync(_factory, sku, name, internalCode, 100m, 5, true);

        // Verify seeded
        using (var scope0 = _factory.Services.CreateScope())
        {
            var db0 = scope0.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
            var countBefore = await db0.Set<Product>().CountAsync(p => p.Sku == sku);
            countBefore.Should().Be(1);
        }

        var adminToken = await IntegrationTestAuthHelper.GetAdminTokenAsync(_factory);
        var duplicateRequest = new ProductModel.ProductRequest(sku, "IC-DUP", "Duplicate Product", "Desc duplicate", 200m, 10);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/products", duplicateRequest);

        // Assert - should be 400 (ArgumentException maps to 400) or 409, but not 500 and not 201
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Conflict);
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
        response.StatusCode.Should().NotBe(HttpStatusCode.Created);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();

        // Verify DB still has only 1 product with that SKU
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var countAfter = await db.Set<Product>().CountAsync(p => p.Sku == sku);
        countAfter.Should().Be(1, "duplicate SKU must not create second row");
    }
}
