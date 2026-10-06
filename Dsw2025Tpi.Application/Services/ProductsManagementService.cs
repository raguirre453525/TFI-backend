using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Dsw2025Tpi.Application.Services;
public class ProductsManagementService
{
    private readonly IRepository _repository;
    public ProductsManagementService(IRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductModel.ProductResponse> AddProduct(ProductModel.ProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku)) throw new ArgumentException("SKU Vacio");

        if(string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Nombre Vacio");

        if(request.CurrentUnitPrice <= 0) throw new ArgumentException("El Precio no puede ser menor o igual a 0");

        if(request.StockQuantity < 0) throw new ArgumentException("El stock no puede ser menor a 0");

        var exist = await _repository.First<Product>(p => p.Sku == request.Sku);
        if (exist != null) throw new ArgumentException($"Ya existe un producto con el Sku: {request.Sku}");

        var product = new Product(
            request.Sku, 
            request.InternalCode ?? string.Empty, 
            request.Name, 
            request.Description ?? string.Empty, 
            request.CurrentUnitPrice, 
            request.StockQuantity
        );

        product.IsActive = true;

        await _repository.Add(product);

        return new ProductModel.ProductResponse(
            product.Id, 
            product.Sku, 
            product.InternalCode, 
            product.Name, 
            product.Description,
            product.CurrentUnitPrice, 
            product.StockQuantity,
            product.IsActive
        );
    }

    public async Task<ProductModel.ResponsePagination?> GetProducts(ProductModel.FilterProduct filter)
    {
        // 1) Convertir estado (enabled/disabled/all)
        bool? isActive = filter.Status switch
        {
            "enabled" => true,
            "disabled" => false,
            _ => null   // all
        };

        // 2) Filtrar desde la base
        var query = await _repository.GetFiltered<Product>(p =>
            (isActive == null || p.IsActive == isActive) &&
            (string.IsNullOrWhiteSpace(filter.Search) || p.Name.Contains(filter.Search)) || p.Sku.Contains(filter.Search)
        );

        if (query == null || !query.Any())
            return null;

        // 3) Preparar valores de paginación
        int pageNumber = filter.PageNumber ?? 1;
        int pageSize = filter.PageSize ?? query.Count();
        int totalCount = query.Count();
        int totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // 4) Aplicar orden + paginar
        var paginated = query
            .OrderBy(p => p.Sku)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductModel.ProductResponse(
                product.Id,
                product.Sku,
                product.InternalCode,
                product.Name,
                product.Description,
                product.CurrentUnitPrice,
                product.StockQuantity,
                product.IsActive
            ))
            .ToList();

        // 5) Respuesta final formato TPI
        return new ProductModel.ResponsePagination(
            paginated,
            totalCount,
            pageNumber,
            pageSize,
            totalPages
        );
    }


    public async Task<ProductModel.ProductResponse?> GetProductById(Guid id)
    {
        var product = await _repository.GetById<Product>(id);

        if (product == null) throw new ProductNotFoundException($"No se encontró el producto");

        return new ProductModel.ProductResponse(
            product.Id,
            product.Sku,
            product.InternalCode,
            product.Name,
            product.Description,
            product.CurrentUnitPrice,
            product.StockQuantity,
            product.IsActive
        );
    }

    public async Task<ProductModel.ProductResponse> PutProduct(Guid id, ProductModel.ProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sku)) throw new ArgumentException("SKU Vacio");

        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Nombre Vacio");

        if (request.CurrentUnitPrice <= 0) throw new ArgumentException("El Precio no puede ser menor o igual a 0");

        if (request.StockQuantity < 0) throw new ArgumentException("El stock no puede ser menor a 0");

        var exist = await _repository.GetById<Product>(id) ?? throw new ProductNotFoundException($"No se encontró el producto");

        //if (!exist.IsActive) throw new ArgumentException($"El producto con ID {id} esta inhabilitado");

        exist.InternalCode = request.InternalCode;
        if (exist.Sku != request.Sku)
        {
            var Skuexist = await _repository.First<Product>(p => p.Sku == request.Sku);
            if (Skuexist != null) throw new ArgumentException($"Ya existe un producto con el Sku: {request.Sku}");
        }
        exist.Sku = request.Sku;
        exist.Name = request.Name;
        exist.Description = request.Description;
        exist.CurrentUnitPrice = request.CurrentUnitPrice;
        exist.StockQuantity = request.StockQuantity;

        await _repository.Update(exist);

        return new ProductModel.ProductResponse(
            exist.Id, 
            exist.Sku, 
            exist.InternalCode, 
            exist.Name, 
            exist.Description,
            exist.CurrentUnitPrice, 
            exist.StockQuantity,
            exist.IsActive
        );
    }

    public async Task InactivateProduct(Guid id)
    {
        var exist = await _repository.GetById<Product>(id)
            ?? throw new ProductNotFoundException($"No se encontró el producto");

        if (!exist.IsActive) 
        { 
            exist.IsActive = true; 
        }
        else
        {
            exist.IsActive = false;
        }
           
        await _repository.Update(exist);
    }
}
