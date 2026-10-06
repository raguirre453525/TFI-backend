using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Dsw2025Tpi.Data;

public class Dsw2025TpiContext: IdentityDbContext
{
    public Dsw2025TpiContext(DbContextOptions<Dsw2025TpiContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // 1. PEGA AQUÍ LA CONFIGURACIÓN DE NOMBRES DE TU OTRO CONTEXTO
        modelBuilder.Entity<IdentityUser>(b => { b.ToTable("Usuarios"); });
        modelBuilder.Entity<IdentityRole>(b => { b.ToTable("Roles"); });
        modelBuilder.Entity<IdentityUserRole<string>>(b => { b.ToTable("UsuariosRoles"); });
        modelBuilder.Entity<IdentityUserClaim<string>>(b => { b.ToTable("UsuariosClaims"); });
        modelBuilder.Entity<IdentityUserLogin<string>>(b => { b.ToTable("UsuariosLogins"); });
        modelBuilder.Entity<IdentityRoleClaim<string>>(b => { b.ToTable("RolesClaims"); });
        modelBuilder.Entity<IdentityUserToken<string>>(b => { b.ToTable("UsuariosTokens"); });



        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("Products");

            product.HasKey(p => p.Id);

            product.Property(p => p.Sku)
                   .HasMaxLength(20)
                   .IsRequired();

            product.HasIndex(p => p.Sku)
                   .IsUnique();

            product.Property(p => p.InternalCode)
                   .HasMaxLength(50);

            product.Property(p => p.Name)
                   .HasMaxLength(60)
                   .IsRequired();

            product.Property(p => p.Description)
                   .HasMaxLength(500);

            product.Property(p => p.CurrentUnitPrice)
                   .HasPrecision(15, 2)
                   .IsRequired();

            product.Property(p => p.StockQuantity)
                   .IsRequired();

            product.Property(p => p.IsActive)
                   .IsRequired();
        });

        modelBuilder.Entity<Order>(order =>
        {
            order.ToTable("Orders");

            order.HasKey(o => o.Id);

            order.Property(o => o.ShippingAddress)
                 .HasMaxLength(200)
                 .IsRequired();

            order.Property(o => o.BillingAddress)
                 .HasMaxLength(200)
                 .IsRequired();

            order.Property(o => o.OrderStatus)
                 .IsRequired();

            order.HasOne(o => o.Customer)
                 .WithMany(c => c.Order)
                 .HasForeignKey(o => o.CustomerId)
                 .OnDelete(DeleteBehavior.Restrict);

            order.HasMany(o => o.OrderItem)
                 .WithOne(oi => oi.Order)
                 .HasForeignKey(oi => oi.OrderId)
                 .OnDelete(DeleteBehavior.Cascade);

            order.Property(o => o.TotalAmount)
                 .HasPrecision(15, 2);

            order.Property(o => o.Date)
                 .HasDefaultValueSql("GETDATE()")
                 .IsRequired();

            order.Property(o => o.Notes);
        });

        modelBuilder.Entity<OrderItem>(item =>
        {
            item.ToTable("OrderItems");

            item.HasKey(i => i.Id);

            /* item.Property(i => i.UnitPrice)
                .HasPrecision(15, 2)
                .IsRequired(); */

            item.Property(i => i.Quantity)
                .IsRequired();

            item.Property(i => i.Subtotal)
                .HasPrecision(15, 2);

            item.HasOne(i => i.Product)
                .WithMany(p => p.OrderItems)
                .HasForeignKey(i => i.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Customer>(customer =>
        {
            // ... config previa ...

            // CORRECCIÓN: Usamos el nuevo campo IdentityUserId
            customer.HasOne(c => c.User)
                    .WithMany() // O WithOne, dependiendo de lo que prefieras, WithMany es más seguro por ahora
                    .HasForeignKey(c => c.IdentityUserId)
                    .IsRequired(true); // Ahora sí es obligatorio tener usuario
        });
    }
}
