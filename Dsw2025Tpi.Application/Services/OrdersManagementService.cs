using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Exceptions;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Enums;
using Dsw2025Tpi.Domain.Interfaces;

namespace Dsw2025Tpi.Application.Services;

public class OrdersManagementService
{
    private readonly IRepository _repository;
    public OrdersManagementService(IRepository repository)
    {
        _repository = repository;
    }

    public async Task<OrderModel.OrderResponse> AddOrder(OrderModel.OrderRequest request)
    {
        // --- 1. Validaciones Iniciales ---
        if (request.CustomerId == Guid.Empty) throw new ArgumentException("Cliente vacío");
        if (string.IsNullOrWhiteSpace(request.ShippingAddress)) throw new ArgumentException("ShippingAddress vacía");
        if (string.IsNullOrWhiteSpace(request.BillingAddress)) throw new ArgumentException("BillingAddress vacía");
        if (request.orderItems == null || !request.orderItems.Any()) throw new ArgumentException("La lista de artículos de la orden no puede estar vacía.");

        // --- 2. Gestión del Cliente ---
        var customer = await _repository.GetById<Customer>(request.CustomerId);

        if (customer is null)
        {
            customer = new Customer
            {
                Id = request.CustomerId,
                IdentityUserId = request.CustomerId.ToString(),
                Name = "Usuario Nuevo",
                Email = "pendiente@actualizar.com"
            };
            await _repository.Add(customer);
        }

        // --- 3. Validación de Stock y Preparación (SIN GUARDAR AÚN) ---
        // Usamos una lista temporal para guardar los productos y sus cantidades validadas
        var productsToUpdate = new List<(Product product, int quantity)>();
        var orderItems = new List<OrderItem>();

        foreach (var item in request.orderItems)
        {
            var product = await _repository.GetById<Product>(item.ProductId);

            // Validaciones
            if (product == null) throw new ArgumentException($"Producto con ID {item.ProductId} no encontrado");
            if (!product.IsActive) throw new ArgumentException($"El producto con ID {item.ProductId} no está activo.");
            if (item.Quantity <= 0) throw new ArgumentException($"La cantidad del producto con ID {item.ProductId} debe ser mayor a cero");

            // Aquí estaba el problema: Si esto falla en el segundo ítem, el primero ya se había guardado.
            if (product.StockQuantity < item.Quantity)
                throw new ArgumentException($"Stock insuficiente para el producto: {product.Name} (ID: {item.ProductId})");

            // Si pasa la validación, lo agregamos a la lista de "pendientes por actualizar"
            // NO hacemos _repository.Update(product) todavía.
            productsToUpdate.Add((product, item.Quantity));

            // Preparamos el OrderItem
            var orderItem = new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                Subtotal = item.Quantity * product.CurrentUnitPrice
            };
            orderItems.Add(orderItem);
        }

        // --- 4. Ejecución de Cambios (Ahora que sabemos que TODO es válido) ---

        // Ahora sí, recorremos los productos validados y descontamos el stock
        foreach (var (product, quantity) in productsToUpdate)
        {
            product.StockQuantity -= quantity;
            await _repository.Update(product); // Aquí persistimos el cambio de stock
        }

        // --- 5. Creación de la Orden ---
        var order = new Order(
            shippingAddress: request.ShippingAddress,
            billingAddress: request.BillingAddress,
            customerId: request.CustomerId,
            orderItems: orderItems
        )
        {
            Date = DateTime.Now,
            TotalAmount = orderItems.Sum(item => item.Subtotal),
            OrderStatus = OrderStatus.Pending
        };

        await _repository.Add(order);

        // --- 6. Respuesta ---
        var orderItemResponses = orderItems
            .Select(orderItem => new OrderItemModel.OrderItemResponse(
                orderItem.ProductId,
                orderItem.Quantity,
                orderItem.Subtotal
            ))
            .ToList();

        return new OrderModel.OrderResponse(
            order.Id,
            request.CustomerId,
            request.ShippingAddress,
            request.BillingAddress,
            order.OrderStatus,
            orderItemResponses,
            order.TotalAmount
        );
    }

    public async Task<IEnumerable<OrderModel.OrderResponse>> GetOrders(OrderModel.SearchOrder request)
    {     
        var orders = await _repository.GetAll<Order>("OrderItem.Product");

        return orders.Select(order => new OrderModel.OrderResponse(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.BillingAddress,
            order.OrderStatus,
            order.OrderItem.Select(item => new OrderItemModel.OrderItemResponse(
                item.ProductId,
                item.Quantity,
                item.Subtotal 
            )).ToList(),
            order.TotalAmount
        ));
    }

    public async Task<OrderModel.OrderResponse?> GetOrderById(Guid id)
    {
        var order = await _repository.GetById<Order>(id, "OrderItem.Product");

        if (order == null) return null;

        return new OrderModel.OrderResponse(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.BillingAddress,
            order.OrderStatus,
            order.OrderItem.Select(item =>
                new OrderItemModel.OrderItemResponse(
                    item.ProductId,
                    item.Quantity,
                    item.Subtotal
                )).ToList(),
            order.TotalAmount
        );
    }
    public async Task<OrderModel.OrderResponse> UpdateOrderStatus(Guid id, string newStatus)
    {      
        var order = await _repository.GetById<Order>(id, "OrderItem.Product");

        if (order == null) throw new OrderNotFoundException("Orden no encontrada.");

        if (!Enum.TryParse<OrderStatus>(newStatus, true, out var parsedStatus) || !Enum.IsDefined(typeof(OrderStatus), parsedStatus))
            throw new ArgumentException("Estado proporcionado no es válido.");

        if (order.OrderStatus == parsedStatus) throw new ArgumentException("La orden ya está en el estado solicitado.");

        order.OrderStatus = parsedStatus;
        await _repository.Update(order);

        return new OrderModel.OrderResponse(
            order.Id,
            order.CustomerId,
            order.ShippingAddress,
            order.BillingAddress,
            order.OrderStatus,
            order.OrderItem.Select(item => new OrderItemModel.OrderItemResponse(
                item.ProductId,
                item.Quantity,
                item.Subtotal
            )).ToList(),
            order.TotalAmount
        );
    }

}
