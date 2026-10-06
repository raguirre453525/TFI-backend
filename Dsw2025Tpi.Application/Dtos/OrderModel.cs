using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dsw2025Tpi.Domain.Enums;

namespace Dsw2025Tpi.Application.Dtos;

public record OrderModel
{
    public record OrderRequest(
        Guid CustomerId, 
        string ShippingAddress, 
        string BillingAddress, 
        List<OrderItemModel.OrderItemRequest> orderItems
    );

    public record OrderResponse(
        Guid OrderId, 
        Guid CustomerId, 
        string ShippingAddress, 
        string BillingAddress, 
        OrderStatus OrderStatus, 
        List<OrderItemModel.OrderItemResponse> orderItems, 
        decimal Total
    );

    public record SearchOrder(
        Guid? CustomerId, 
        string? Status, 
        int PageNumber = 1, 
        int PageSize = 10

        );
}
