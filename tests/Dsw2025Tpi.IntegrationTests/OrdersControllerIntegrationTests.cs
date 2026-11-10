using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

[Collection("IntegrationTests")]
public class OrdersControllerIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public OrdersControllerIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateOrder_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var uniqueShipping = $"Ship-T4-{Guid.NewGuid():N}"[..18];
        var orderRequest = new OrderModel.OrderRequest(
            Guid.NewGuid(),
            uniqueShipping,
            "Billing-T4",
            new List<OrderItemModel.OrderItemRequest>
            {
                new OrderItemModel.OrderItemRequest(Guid.NewGuid(), 1)
            });

        var client = _factory.CreateClient();

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", orderRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Verify no order persisted
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var persisted = await db.Set<Order>().FirstOrDefaultAsync(o => o.ShippingAddress == uniqueShipping);
        persisted.Should().BeNull("unauthenticated order must not be persisted");
    }

    [Fact]
    public async Task CreateOrder_WhenAuthenticatedWithValidData_ReturnsCreatedAndPersistsOrderAndStockInSqlite()
    {
        // Arrange - create USER with Customer, and product stock 10 price 100
        var (userToken, customerId) = await IntegrationTestAuthHelper.GetUserTokenWithCustomerAsync(_factory);

        var sku = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T5");
        var product = await DatabaseHelper.SeedProductAsync(_factory, sku, $"TP4-T5 Prod {Guid.NewGuid():N}"[..16], $"IC-T5-{Guid.NewGuid():N}"[..10], 100m, 10, true);
        var productId = product.Id;

        var uniqueShipping = $"Ship-T5-{Guid.NewGuid():N}"[..18];
        var uniqueBilling = $"Bill-T5-{Guid.NewGuid():N}"[..18];
        var orderRequest = new OrderModel.OrderRequest(
            customerId,
            uniqueShipping,
            uniqueBilling,
            new List<OrderItemModel.OrderItemRequest>
            {
                new OrderItemModel.OrderItemRequest(productId, 3)
            });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", orderRequest);

        // Assert - HTTP
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("api/orders");

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var body = await response.Content.ReadFromJsonAsync<OrderModel.OrderResponse>(jsonOptions);
        body.Should().NotBeNull();
        body!.CustomerId.Should().Be(customerId);
        body.ShippingAddress.Should().Be(uniqueShipping);
        body.BillingAddress.Should().Be(uniqueBilling);
        body.orderItems.Should().HaveCount(1);
        body.orderItems[0].ProductId.Should().Be(productId);
        body.orderItems[0].Quantity.Should().Be(3);
        body.Total.Should().Be(300m);

        // Verify persistence in SQLite
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var persistedOrder = await db.Set<Order>()
            .Include(o => o.OrderItem)
            .FirstOrDefaultAsync(o => o.ShippingAddress == uniqueShipping);
        persistedOrder.Should().NotBeNull();
        persistedOrder!.CustomerId.Should().Be(customerId);
        persistedOrder.ShippingAddress.Should().Be(uniqueShipping);
        persistedOrder.BillingAddress.Should().Be(uniqueBilling);
        persistedOrder.OrderItem.Should().HaveCount(1);
        var item = persistedOrder.OrderItem.First();
        item.ProductId.Should().Be(productId);
        item.Quantity.Should().Be(3);
        item.Subtotal.Should().Be(300m);
        persistedOrder.TotalAmount.Should().Be(300m);

        // Verify stock decremented: 10 -> 7
        var persistedProduct = await db.Set<Product>().FirstOrDefaultAsync(p => p.Id == productId);
        persistedProduct.Should().NotBeNull();
        persistedProduct!.StockQuantity.Should().Be(7);
    }

    [Fact]
    public async Task CreateOrder_WhenStockIsInsufficient_ReturnsBadRequestWithErrorMessage()
    {
        // Arrange - create USER+Customer, product stock 2
        var (userToken, customerId) = await IntegrationTestAuthHelper.GetUserTokenWithCustomerAsync(_factory);

        var sku = IntegrationTestAuthHelper.GenerateUniqueSku("TP4-T6");
        var product = await DatabaseHelper.SeedProductAsync(_factory, sku, $"TP4-T6 Prod {Guid.NewGuid():N}"[..16], $"IC-T6-{Guid.NewGuid():N}"[..10], 50m, 2, true);
        var productId = product.Id;

        var uniqueShipping = $"Ship-T6-{Guid.NewGuid():N}"[..18];
        var orderRequest = new OrderModel.OrderRequest(
            customerId,
            uniqueShipping,
            "Billing-T6",
            new List<OrderItemModel.OrderItemRequest>
            {
                new OrderItemModel.OrderItemRequest(productId, 5)
            });

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

        // Act
        var response = await client.PostAsJsonAsync("/api/orders", orderRequest);

        // Assert - 400 with Stock insuficiente message (case-insensitive)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrWhiteSpace();
        content.ToLowerInvariant().Should().Contain("stock insuficiente");

        // Verify no order created
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        var persistedOrder = await db.Set<Order>().FirstOrDefaultAsync(o => o.ShippingAddress == uniqueShipping);
        persistedOrder.Should().BeNull("order with insufficient stock must not be persisted");

        // Verify stock stays 2
        var persistedProduct = await db.Set<Product>().FirstOrDefaultAsync(p => p.Id == productId);
        persistedProduct.Should().NotBeNull();
        persistedProduct!.StockQuantity.Should().Be(2, "stock must remain unchanged when order fails");
    }
}
