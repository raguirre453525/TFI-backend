
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dsw2025Tpi.Application.Dtos;

public record ProductModel
{
    public record ProductRequest(
        string Sku, 
        string? InternalCode, 
        string Name, 
        string? Description, 
        decimal CurrentUnitPrice, 
        int StockQuantity
    );

    public record ProductResponse(
        Guid Id, 
        string Sku, 
        string? InternalCode, 
        string? Name, 
        string? Description, 
        decimal CurrentUnitPrice, 
        int StockQuantity,
        bool IsActive
    );

    public record ResponsePagination(
        List<ProductResponse> Items,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages
    );

    public record FilterProduct(
        string? Status, 
        string? Search, 
        int? PageNumber, 
        int? PageSize
    );

}
