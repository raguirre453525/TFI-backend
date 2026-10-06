using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2025Tpi.Api.Controllers;

[ApiController]
[Route("api/orders")]
public class OrdersController : ControllerBase
{
    private readonly OrdersManagementService _service;
    public OrdersController(OrdersManagementService service)
    {
        _service = service;
    }

    /// <summary>
    /// CREAR UNA NUEVA ORDEN
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "USER,ADMIN")]
    public async Task<IActionResult> AddOrder([FromBody] OrderModel.OrderRequest request)
    {
        try
        {
            var order = await _service.AddOrder(request);
            return Created($"/api/orders/{order.OrderId}", order);
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
    /// OBTENER TODAS LAS ORDENES
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "USER,ADMIN")]
    public async Task<IActionResult> GetOrders([FromQuery] OrderModel.SearchOrder request)
    {
        try
        {
            var orders = await _service.GetOrders(request);
            
            if (!orders.Any()) return NoContent();

            return Ok(orders);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }

    /// <summary>
    /// OBTENER UNA ORDEN POR ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = "USER,ADMIN")]
    public async Task<IActionResult> GetOrderById(Guid id)
    {
        try
        {
            var order = await _service.GetOrderById(id);

            if (order == null) return NotFound();

            return Ok(order);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }

    /// <summary>
    /// ACTUALIZAR EL ESTADO DE UNA ORDEN
    /// </summary>
    /// <remarks>
    /// Estados posibles: Pending, Processing, Shipped, Delivered o Canceled.
    /// </remarks>
    [HttpPut("{id}/status")]
    [Authorize(Roles = "ADMIN")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusModel.UpdateOrderStatusRequest request)
    {
        try
        {
            var result = await _service.UpdateOrderStatus(id, request.NewStatus);
            return Ok(result);
        }
        catch (ArgumentException ae)
        {
            return BadRequest(ae.Message);
        }
        catch (OrderNotFoundException ae)
        {
            return NotFound(ae.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Ocurrió un error inesperado");
        }
    }
}
