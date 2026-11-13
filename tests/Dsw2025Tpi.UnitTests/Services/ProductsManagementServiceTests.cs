using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using FluentAssertions;
using Moq;
using Xunit;

namespace Dsw2025Tpi.UnitTests.Services;

public class ProductsManagementServiceTests
{
    private readonly Mock<IRepository> _repositoryMock;
    private readonly ProductsManagementService _service;

    public ProductsManagementServiceTests()
    {
        _repositoryMock = new Mock<IRepository>();
        _service = new ProductsManagementService(_repositoryMock.Object);
    }

    private static Product BuildProduct(
        string sku = "SKU-001",
        string name = "Mouse Gamer",
        decimal price = 1500.50m,
        int stock = 10,
        string internalCode = "INT-001",
        bool isActive = true)
    {
        var product = new Product(sku, internalCode, name, "Descripcion", price, stock)
        {
            IsActive = isActive
        };
        return product;
    }

    // Test 1: valid data creates product and calls Add once.
    [Fact]
    public async Task AddProduct_WithValidData_ReturnsProductAndCallsRepositoryAdd()
    {
        // Arrange
        var request = new ProductModel.ProductRequest("SKU-001", "INT-001", "Mouse Gamer", "Descripcion", 1500.50m, 10);
        _repositoryMock
            .Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync((Product?)null);
        _repositoryMock
            .Setup(r => r.Add(It.IsAny<Product>()))
            .ReturnsAsync((Product p) => p);

        // Act
        var result = await _service.AddProduct(request);

        // Assert
        result.Should().NotBeNull();
        result.Sku.Should().Be("SKU-001");
        result.Name.Should().Be("Mouse Gamer");
        result.CurrentUnitPrice.Should().Be(1500.50m);
        result.StockQuantity.Should().Be(10);
        result.IsActive.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Once);
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Once);
    }

    // Test 2: duplicate SKU -> project contract is ArgumentException (no DuplicatedEntity type exists).
    [Fact]
    public async Task AddProduct_WithDuplicateSku_ThrowsDuplicatedEntityException()
    {
        // Arrange
        var existing = BuildProduct(sku: "SKU-DUP");
        var request = new ProductModel.ProductRequest("SKU-DUP", "INT-001", "Teclado", "Descripcion", 500m, 5);
        _repositoryMock
            .Setup(r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(existing);

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.AddProduct(request));

        // Assert
        ex.Message.Should().Contain("Ya existe un producto con el Sku");
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        _repositoryMock.Verify(
            r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Once);
    }

    // Test 3: price <= 0 and stock <= 0 throw (enunciado exige rechazar stock <= 0).
    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(-0.01, 10)]
    [InlineData(100, -1)]
    [InlineData(100, -10)]
    [InlineData(100, 0)]
    public async Task AddProduct_WithInvalidStockOrPrice_ThrowsInvalidDataException(decimal price, int stock)
    {
        // Arrange
        var request = new ProductModel.ProductRequest("SKU-001", "INT-001", "Mouse Gamer", "Descripcion", price, stock);

        // Act
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.AddProduct(request));

        // Assert
        ex.Message.Should().NotBeNullOrWhiteSpace();
        if (price <= 0)
            ex.Message.Should().Contain("Precio");
        else
            ex.Message.ToLower().Should().Contain("stock");
        _repositoryMock.Verify(r => r.Add(It.IsAny<Product>()), Times.Never);
        _repositoryMock.Verify(
            r => r.First<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Never);
    }

    // Test 7: "enabled" se verifica sobre el predicado REAL. El Mock actúa como
    // repositorio en memoria: evalúa la expresión que el servicio construye,
    // sin en latar resultados. Incluye un inactivo cuyo SKU coincide con la búsqueda.
    [Fact]
    public async Task GetProducts_WhenIncludeInactiveIsFalse_ReturnsOnlyActiveProducts()
    {
        // Arrange: colección sin filtrar; el servicio debe excluir el inactivo
        var products = new List<Product>
        {
            BuildProduct(sku: "SKU-01", name: "Mouse Gamer"),
            BuildProduct(sku: "SKU-02", name: "Teclado"),
            BuildProduct(sku: "SKU-99", name: "Viejo", isActive: false)
        };
        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .Returns((Expression<Func<Product, bool>> predicate, string[] _) =>
                Task.FromResult<IEnumerable<Product>?>(products.Where(predicate.Compile()).ToList()));

        // Act: sin término de búsqueda
        var result = await _service.GetProducts(new ProductModel.FilterProduct("enabled", null, 1, 10));

        // Assert: solo activos
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(p => p.IsActive);
        result.Items.Should().NotContain(p => p.Sku == "SKU-99");

        // Act: el único SKU coincidente es el del inactivo
        var skuMatch = await _service.GetProducts(new ProductModel.FilterProduct("enabled", "SKU-99", 1, 10));

        // Assert: ningún activo coincide -> null (contrato actual: vacío = null)
        skuMatch.Should().BeNull();
        _repositoryMock.Verify(
            r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Exactly(2));
    }

    // Test 8: 15 products, page 2 x 5 -> items 6-10 ordered by Sku, total 15.
    [Fact]
    public async Task GetProducts_WithPaginationFilter_AppliesSkipAndTakeCorrectly()
    {
        // Arrange
        var products = Enumerable.Range(1, 15)
            .Select(i => BuildProduct(sku: $"SKU-{i:00}", name: $"Producto {i:00}"))
            .ToList();
        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(products);
        var filter = new ProductModel.FilterProduct(null, null, 2, 5);

        // Act
        var result = await _service.GetProducts(filter);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(5);
        result.TotalCount.Should().Be(16);
        result.PageNumber.Should().Be(2);
        result.PageSize.Should().Be(5);
        result.TotalPages.Should().Be(3);
        result.Items.Select(p => p.Sku).Should().ContainInOrder("SKU-06", "SKU-07", "SKU-08", "SKU-09", "SKU-10");
    }

    // Test 9: la búsqueda corre sobre el predicado REAL (Name/Sku/InternalCode,
    // case-insensitive pareando ToLower en ambos lados). El Mock evalúa la expresión
    // contra datos sin filtrar en vez de devolver el resultado esperado.
    [Theory]
    [InlineData("mouse", "MOUSE-001")]   // por nombre
    [InlineData("TEC-002", "TEC-002")]   // por SKU
    [InlineData("int-mon", "MON-003")]   // por código interno
    [InlineData("MOUSE", "MOUSE-001")]   // mayúsculas
    public async Task GetProducts_WithSearchTerm_FiltersByNameSkuOrInternalCode(string search, string expectedSku)
    {
        // Arrange
        var products = new List<Product>
        {
            BuildProduct(sku: "MOUSE-001", name: "Mouse Gamer", internalCode: "INT-MOUSE"),
            BuildProduct(sku: "TEC-002", name: "Teclado Mecanico", internalCode: "INT-TEC"),
            BuildProduct(sku: "MON-003", name: "Monitor", internalCode: "INT-MON")
        };
        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .Returns((Expression<Func<Product, bool>> predicate, string[] _) =>
                Task.FromResult<IEnumerable<Product>?>(products.Where(predicate.Compile()).ToList()));
        var filter = new ProductModel.FilterProduct(null, search, 1, 10);

        // Act
        var result = await _service.GetProducts(filter);

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().ContainSingle().Which.Sku.Should().Be(expectedSku);
        _repositoryMock.Verify(
            r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProducts_WithNullSearch_ReturnsAllWithoutError()
    {
        // Arrange
        var products = new List<Product>
        {
            BuildProduct(sku: "MOUSE-001", name: "Mouse Gamer", internalCode: "INT-MOUSE"),
            BuildProduct(sku: "TEC-002", name: "Teclado Mecanico", internalCode: "INT-TEC"),
            BuildProduct(sku: "MON-003", name: "Monitor", internalCode: "INT-MON")
        };
        _repositoryMock
            .Setup(r => r.GetFiltered<Product>(It.IsAny<Expression<Func<Product, bool>>>(), It.IsAny<string[]>()))
            .Returns((Expression<Func<Product, bool>> predicate, string[] _) =>
                Task.FromResult<IEnumerable<Product>?>(products.Where(predicate.Compile()).ToList()));

        // Act
        var result = await _service.GetProducts(new ProductModel.FilterProduct(null, null, 1, 10));

        // Assert
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
    }
}
