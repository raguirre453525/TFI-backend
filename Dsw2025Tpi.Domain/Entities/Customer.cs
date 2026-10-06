using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace Dsw2025Tpi.Domain.Entities;
public class Customer : EntityBase
{
    public string? Email { get; set; }
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }

    public string IdentityUserId { get; set; }
    // RELACIÓN NUEVA (CONSEJO DEL PROFE)
    // Esto indica que el Customer está vinculado a un Usuario del Login
    [ForeignKey("IdentityUserId")]
    public IdentityUser? User { get; set; }

    public ICollection<Order> Order { get; set; } = new List<Order>();
}
