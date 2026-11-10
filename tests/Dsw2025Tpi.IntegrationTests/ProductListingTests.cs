using System.Net;
using System.Net.Http.Json;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

// Cobertura de traducción REAL del predicado de GetProducts (EF Core + SQLite).
// No sustituye a los unitarios de TP4-02: acá se prueba que la expresión también
// corre contra un proveedor relacional y no solo compilada en memoria.
[Collection("IntegrationTests")]
public class ProductListingTests
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ProductListingTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedAsync(params Product[] products)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        db.Set<Product>().AddRange(products);
        await db.SaveChangesAsync();
    }

    private static Product P(string sku, string internalCode, string name, bool active = true) =>
        new(sku, internalCode, name, "Descripcion", 100m, 5) { IsActive = active };

    [Fact]
    public async Task GetProducts_EnabledExcludesInactiveEvenWhenSkuMatches()
    {
        await SeedAsync(
            P("TP7-ACT", "INT-TP7-A", "Tp7 Teclado Uno"),
            P("TP7-INACT", "INT-TP7-B", "Tp7 Viejo", active: false));

        // El único SKU coincidente es el del inactivo: debe dar vacío (204).
        var onlyInactive = await _client.GetAsync("/api/products?search=TP7-INACT");
        onlyInactive.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Búsqueda amplia: solo activos en la respuesta.
        var all = await _client.GetAsync("/api/products?search=tp7");
        all.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await all.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        page.Should().NotBeNull();
        page!.Items.Should().OnlyContain(p => p.IsActive);
        page.Items.Should().ContainSingle(p => p.Sku == "TP7-ACT");
    }

    [Fact]
    public async Task GetProducts_SearchMatchesNameSkuAndInternalCodeCaseInsensitive()
    {
        await SeedAsync(
            P("TP9-A001", "TP9-IA", "Tp9-Mouse Gamer"),
            P("TP9-B002", "TP9-IB", "Tp9 Teclado"),
            P("TP9-C003", "TP9-IMON", "Tp9 Monitor"));

        foreach (var (search, expectedSku) in new[]
                 {
                     ("tp9-mouse", "TP9-A001"),  // por nombre, minúsculas
                     ("TP9-B002", "TP9-B002"),   // por SKU, mayúsculas
                     ("tp9-imon", "TP9-C003"),   // por código interno
                     ("TP9-MOUSE", "TP9-A001")   // mayúsculas contra nombre mixto
                 })
        {
            var response = await _client.GetAsync($"/api/products?search={search}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
            page.Should().NotBeNull();
            page!.Items.Should().ContainSingle().Which.Sku.Should().Be(expectedSku);
        }
    }

    [Fact]
    public async Task GetProducts_WithoutSearch_ReturnsActivesWithoutError()
    {
        var response = await _client.GetAsync("/api/products");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        page.Should().NotBeNull();
        page!.Items.Should().OnlyContain(p => p.IsActive);
        page.Items.Should().Contain(p => p.Sku == "TP7-ACT");
        page.Items.Should().NotContain(p => p.Sku == "TP7-INACT");
    }
}
