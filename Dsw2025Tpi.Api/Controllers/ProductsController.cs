using System.Runtime.ConstrainedExecution;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2025Tpi.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly ProductsManagementService _service;
    public ProductsController(ProductsManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// CREAR UN NUEVO PRODUCTO
    /// </summary>
    [HttpPost()]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> AddProduct([FromBody] ProductModel.ProductRequest request)
    {
        try
        {
            var product = await _service.AddProduct(request);
            return Created($"api/products/{product.Id}", product);
        }
        catch (ArgumentException ae)
        {
            return BadRequest(ae.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }

    /// <summary>
    /// OBTENER TODOS LOS PRODUCTOS
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetActiveProducts([FromQuery] ProductModel.FilterProduct filter)
    {
        filter = filter with { Status = "enabled" };

        var products = await _service.GetProducts(filter);

        if (products == null)
            return NoContent();

        return Ok(products);
    }


    [HttpGet("admin")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> GetAdminProducts([FromQuery] ProductModel.FilterProduct filter)
    {
        var products = await _service.GetProducts(filter);

        if (products == null)
            return NoContent();

        return Ok(products);
    }

    /// <summary>
    /// OBTENER UN PRODUCTO POR ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetProductById(Guid id)
    {
        try
        { 
            var product = await _service.GetProductById(id);


            return Ok(product);
        }
        catch (ArgumentException ae)
        {
            return BadRequest(ae.Message);
        }
        catch (ProductNotFoundException ae)
        {
            return NotFound(ae.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }

    /// <summary>
    /// ACTUALIZAR UN PRODUCTO POR ID
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> PutProduct(Guid id, [FromBody] ProductModel.ProductRequest request)
    {
        try
        {
            var product = await _service.PutProduct(id, request);
            return Ok(product);
        }
        catch (ArgumentException ae)
        {
            return BadRequest(ae.Message);
        }
        catch (ProductNotFoundException ae)
        {
            return NotFound(ae.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }

    /// <summary>
    /// INHABILITAR/HABILITAR UN PRODUCTO POR ID
    /// </summary>
    [HttpPatch("{id}")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> InactivateProduct(Guid id)
    {
        try
        {
            await _service.InactivateProduct(id);
            return NoContent(); 
        }
        catch (ProductNotFoundException ae)
        {
            return NotFound(ae.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }
}
