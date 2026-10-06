using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dsw2025Tpi.Domain.Enums;

namespace Dsw2025Tpi.Domain.Entities;
public class Order : EntityBase
{
    public Order() { }
    public Order(string shippingAddress, string billingAddress, Guid customerId, List<OrderItem> orderItems)
    {
        ShippingAddress = shippingAddress;
        BillingAddress = billingAddress;
        CustomerId = customerId;
        OrderItem = orderItems;
    }

    public DateTime Date { get; set; }
    public string? Notes { get; set; }
    public string ShippingAddress { get; set; }
    public string BillingAddress { get; set; }
    public decimal TotalAmount { get; set; }

    public ICollection<OrderItem> OrderItem { get; set; } = new List<OrderItem>();
    public Customer Customer { get; set; }
    public OrderStatus OrderStatus { get; set; }
    public Guid CustomerId { get; set; }
}
