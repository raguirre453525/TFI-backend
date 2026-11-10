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
public class ProductsListingIntegrationTests
{
    private readonly CustomWebApplicationFactory _factory;

    public ProductsListingIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProducts_PublicEndpoint_ReturnsOkWithPaginatedJsonStructure()
    {
        // Arrange - seed product with unique SKU/prefix to isolate from other tests sharing the same SQLite in-memory DB
        // CustomWebApplicationFactory uses a single SqliteConnection (Cache=Shared) shared across all [Collection("IntegrationTests")] tests.
        // Isolation is achieved via unique SKUs/prefixes, not via EnsureDeleted (which would wipe TP7 data needed by pre-existing ProductListingTests).
        var uniquePrefix = $"T7{Guid.NewGuid():N}"[..6].ToUpperInvariant();
        var sku = IntegrationTestAuthHelper.GenerateUniqueSku(uniquePrefix);
        var name = $"T7 Pub {uniquePrefix} {Guid.NewGuid():N}"[..18];
        var ic = $"{uniquePrefix}-IC";
        await DatabaseHelper.SeedProductAsync(_factory, sku, name, ic, 120m, 5, true);

        var client = _factory.CreateClient();

        // Act - unfiltered public listing (spec: must find product without filters)
        var response = await client.GetAsync("/api/products");

        // Assert - public endpoint structure
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var page = await response.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        page.Should().NotBeNull();
        page!.Items.Should().NotBeNullOrEmpty();
        page.TotalCount.Should().BeGreaterThan(0);
        page.PageNumber.Should().BeGreaterThan(0);
        page.PageSize.Should().BeGreaterThan(0);
        page.TotalPages.Should().BeGreaterThan(0);
        page.Items.Should().OnlyContain(p => p.IsActive, "public endpoint must return only active products");
        // Verify seeded product is present in unfiltered list (order-independent, pagination returns all when no PageSize)
        page.Items.Should().Contain(p => p.Sku == sku);

        // Additional isolation check: filtered search must return exclusively the seeded product (reproducible regardless of other tests' data volume)
        var responseFiltered = await client.GetAsync($"/api/products?search={Uri.EscapeDataString(sku)}");
        responseFiltered.StatusCode.Should().Be(HttpStatusCode.OK);
        responseFiltered.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        var pageFiltered = await responseFiltered.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageFiltered.Should().NotBeNull();
        pageFiltered!.Items.Should().ContainSingle().Which.Sku.Should().Be(sku);
        pageFiltered.TotalCount.Should().Be(1);
        pageFiltered.Items[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetProducts_WithQueryStringFilters_ReturnsFilteredResultsFromSqlite()
    {
        // Arrange - insert 3 products with distinct names/SKUs/internalCodes under unique prefix
        var uniquePrefix = $"F{Guid.NewGuid():N}"[..6].ToUpperInvariant(); // e.g., F8A2B3
        var sku1 = IntegrationTestAuthHelper.GenerateUniqueSku($"{uniquePrefix}A");
        var sku2 = IntegrationTestAuthHelper.GenerateUniqueSku($"{uniquePrefix}B");
        var sku3 = IntegrationTestAuthHelper.GenerateUniqueSku($"{uniquePrefix}C");

        var name1 = $"{uniquePrefix} Alpha Mouse";
        var name2 = $"{uniquePrefix} Beta Keyboard";
        var name3 = $"{uniquePrefix} Gamma Monitor";

        var ic1 = $"{uniquePrefix}-IC-A";
        var ic2 = $"{uniquePrefix}-IC-B";
        var ic3 = $"{uniquePrefix}-IC-MON";

        await DatabaseHelper.SeedProductAsync(_factory, sku1, name1, ic1, 100m, 5, true);
        await DatabaseHelper.SeedProductAsync(_factory, sku2, name2, ic2, 150m, 5, true);
        await DatabaseHelper.SeedProductAsync(_factory, sku3, name3, ic3, 200m, 5, true);

        var client = _factory.CreateClient();

        // Act - filtered by prefix, paginated PageNumber=1 PageSize=2
        var responsePaged = await client.GetAsync($"/api/products?search={uniquePrefix}&PageNumber=1&PageSize=2");
        responsePaged.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1 = await responsePaged.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        page1.Should().NotBeNull();
        page1!.Items.Should().HaveCount(2, "PageSize=2 should return 2 items");
        page1.TotalCount.Should().Be(3, "filtered total should be 3 for unique prefix");
        page1.PageNumber.Should().Be(1);
        page1.PageSize.Should().Be(2);
        page1.TotalPages.Should().Be(2, "3 items with PageSize 2 => 2 pages");
        page1.Items.Should().OnlyContain(p => p.IsActive);

        // Act - second page
        var responsePaged2 = await client.GetAsync($"/api/products?search={uniquePrefix}&PageNumber=2&PageSize=2");
        responsePaged2.StatusCode.Should().Be(HttpStatusCode.OK);
        var page2 = await responsePaged2.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        page2.Should().NotBeNull();
        page2!.Items.Should().HaveCount(1);
        page2.TotalCount.Should().Be(3);
        page2.PageNumber.Should().Be(2);

        // Act - search by specific name substring using unique term to avoid collisions with other tests
        var searchAlpha = $"{uniquePrefix} Alpha";
        var responseAlpha = await client.GetAsync($"/api/products?search={Uri.EscapeDataString(searchAlpha)}&PageNumber=1&PageSize=10");
        responseAlpha.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageAlpha = await responseAlpha.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageAlpha.Should().NotBeNull();
        pageAlpha!.Items.Should().ContainSingle("search by unique name term must return exclusively the expected product");
        pageAlpha.Items[0].Name.Should().Be(name1);
        pageAlpha.Items[0].Sku.Should().Be(sku1);
        pageAlpha.TotalCount.Should().Be(1);

        // Act - search by SKU (reliable)
        var responseBySku = await client.GetAsync($"/api/products?search={sku1}");
        responseBySku.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageSku = await responseBySku.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageSku.Should().NotBeNull();
        pageSku!.Items.Should().ContainSingle().Which.Sku.Should().Be(sku1);

        // Act - search by internalCode
        var responseByIc = await client.GetAsync($"/api/products?search={ic3}");
        responseByIc.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageIc = await responseByIc.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageIc.Should().NotBeNull();
        pageIc!.Items.Should().ContainSingle().Which.InternalCode.Should().Be(ic3);

        // Verify pagination without search returns our 3 ordered by SKU (implicit pagination check)
        // Already covered above
    }

    [Fact]
    public async Task GetAuthProducts_AdminEndpoint_WhenCalledByAdmin_IncludesInactiveProducts()
    {
        // Arrange - insert active+inactive products with unique SKUs
        var uniquePrefix = $"AD{Guid.NewGuid():N}"[..6].ToUpperInvariant(); // e.g., AD8A2B
        var skuActive = IntegrationTestAuthHelper.GenerateUniqueSku($"{uniquePrefix}A");
        var skuInactive = IntegrationTestAuthHelper.GenerateUniqueSku($"{uniquePrefix}I");

        var nameActive = $"{uniquePrefix} Active Prod";
        var nameInactive = $"{uniquePrefix} Inactive Prod";

        var icActive = $"{uniquePrefix}-IC-ACT";
        var icInactive = $"{uniquePrefix}-IC-INACT";

        await DatabaseHelper.SeedProductAsync(_factory, skuActive, nameActive, icActive, 100m, 5, true);
        await DatabaseHelper.SeedProductAsync(_factory, skuInactive, nameInactive, icInactive, 100m, 5, false);

        var adminToken = await IntegrationTestAuthHelper.GetAdminTokenAsync(_factory);

        var clientAdmin = _factory.CreateClient();
        clientAdmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Act - GET /api/products/admin as ADMIN with search prefix
        var responseAdmin = await clientAdmin.GetAsync($"/api/products/admin?search={uniquePrefix}");
        responseAdmin.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageAdmin = await responseAdmin.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageAdmin.Should().NotBeNull();
        pageAdmin!.Items.Should().Contain(p => p.Sku == skuActive);
        pageAdmin.Items.Should().Contain(p => p.Sku == skuInactive);
        pageAdmin.Items.Should().Contain(p => !p.IsActive, "admin endpoint must include inactive");
        pageAdmin.TotalCount.Should().Be(2);

        // Act - GET /api/products as anonymous with same search
        var clientAnon = _factory.CreateClient();
        var responseAnon = await clientAnon.GetAsync($"/api/products?search={uniquePrefix}");

        // Anonymous public endpoint should return only active
        // It may return 200 with single active item
        responseAnon.StatusCode.Should().Be(HttpStatusCode.OK);
        var pageAnon = await responseAnon.Content.ReadFromJsonAsync<ProductModel.ResponsePagination>();
        pageAnon.Should().NotBeNull();
        pageAnon!.Items.Should().ContainSingle().Which.Sku.Should().Be(skuActive);
        pageAnon.Items.Should().OnlyContain(p => p.IsActive);
        pageAnon.TotalCount.Should().Be(1);
        pageAnon.Items.Should().NotContain(p => p.Sku == skuInactive);

        // Additional contrast: admin endpoint without auth should be 401
        var responseAdminNoAuth = await clientAnon.GetAsync("/api/products/admin");
        responseAdminNoAuth.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
