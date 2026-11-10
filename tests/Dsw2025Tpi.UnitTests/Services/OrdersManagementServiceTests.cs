using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests.Services;

public class OrdersManagementServiceTests
{
    private readonly Mock<IRepository> _repositoryMock;
    private readonly OrdersManagementService _service;

    public OrdersManagementServiceTests()
    {
        _repositoryMock = new Mock<IRepository>();
        _service = new OrdersManagementService(_repositoryMock.Object);
    }

    private static Customer BuildCustomer(Guid id) =>
        new()
        {
            Id = id,
            IdentityUserId = id.ToString(),
            Name = "Cliente Test",
            Email = "cliente@test.com"
        };

    private static Product BuildProduct(Guid id, string name, decimal price, int stock)
    {
        var product = new Product($"SKU-{id.ToString()[..8].ToUpper()}", "INT-001", name, "Descripcion", price, stock)
        {
            IsActive = true
        };
        product.Id = id;
        return product;
    }

    // Test 4: valid items + stock -> total = sum of subtotals, stock decremented, order persisted.
    [Fact]
    public async Task CreateOrder_WithValidItemsAndStock_CalculatesTotalAndDecrementsProductStock()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId1 = Guid.NewGuid();
        var productId2 = Guid.NewGuid();
        var customer = BuildCustomer(customerId);
        var product1 = BuildProduct(productId1, "Mouse Gamer", 100m, 10);
        var product2 = BuildProduct(productId2, "Teclado", 50m, 20);

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        _repositoryMock
            .Setup(r => r.GetById<Product>(productId1, It.IsAny<string[]>()))
            .ReturnsAsync(product1);
        _repositoryMock
            .Setup(r => r.GetById<Product>(productId2, It.IsAny<string[]>()))
            .ReturnsAsync(product2);
        _repositoryMock
            .Setup(r => r.Update(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);
        _repositoryMock
            .Setup(r => r.Add(It.IsAny<Order>()))
            .ReturnsAsync((Order o) => o);

        var request = new OrderModel.OrderRequest(
            customerId,
            "Shipping 123",
            "Billing 123",
            new List<OrderItemModel.OrderItemRequest>
            {
                new(productId1, 2),
                new(productId2, 3)
            });

        // Act
        var result = await _service.AddOrder(request);

        // Assert
        result.Should().NotBeNull();
        result.Total.Should().Be(350m);
        result.orderItems.Should().HaveCount(2);
        result.orderItems.Should().ContainSingle(i => i.ProductId == productId1 && i.Quantity == 2 && i.Subtotal == 200m);
        result.orderItems.Should().ContainSingle(i => i.ProductId == productId2 && i.Quantity == 3 && i.Subtotal == 150m);
        product1.StockQuantity.Should().Be(8);
        product2.StockQuantity.Should().Be(17);
        _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => p.Id == productId1)), Times.Once);
        _repositoryMock.Verify(r => r.Update(It.Is<Product>(p => p.Id == productId2)), Times.Once);
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Once);
    }

    // Test 5: stock 2, request 5 -> ArgumentException, no order saved, no stock update.
    [Fact]
    public async Task CreateOrder_WhenProductStockIsInsufficient_ThrowsInsufficientStockException()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var customer = BuildCustomer(customerId);
        var product = BuildProduct(productId, "Mouse Gamer", 100m, 2);

        _repositoryMock
            .Setup(r => r.GetById<Customer>(customerId, It.IsAny<string[]>()))
            .ReturnsAsync(customer);
        _repositoryMock
            .Setup(r => r.GetById<Product>(productId, It.IsAny<string[]>()))
            .ReturnsAsync(product);

        var request = new OrderModel.OrderRequest(
            customerId,
            "Shipping 123",
            "Billing 123",
            new List<OrderItemModel.OrderItemRequest> { new(productId, 5) });

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.AddOrder(request));

        // Assert
        ex.Message.Should().Contain("Stock insuficiente");
        product.StockQuantity.Should().Be(2);
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        _repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
    }

    // Test 6: unknown customer -> CustomerNotFoundException, no order, no product updates.
    [Fact]
    public async Task CreateOrder_WhenCustomerDoesNotExist_ThrowsCustomerNotFoundException()
    {
        // Arrange
        var unknownCustomerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetById<Customer>(unknownCustomerId, It.IsAny<string[]>()))
            .ReturnsAsync((Customer?)null);

        var request = new OrderModel.OrderRequest(
            unknownCustomerId,
            "Shipping 123",
            "Billing 123",
            new List<OrderItemModel.OrderItemRequest> { new(productId, 1) });

        // Act
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => _service.AddOrder(request));

        // Assert
        _repositoryMock.Verify(r => r.Add(It.IsAny<Order>()), Times.Never);
        _repositoryMock.Verify(r => r.Add(It.IsAny<Customer>()), Times.Never);
        _repositoryMock.Verify(r => r.Update(It.IsAny<Product>()), Times.Never);
    }
}
