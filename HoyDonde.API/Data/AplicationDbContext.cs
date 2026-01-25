using HoyDonde.API.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Admin> Admins { get; set; }
        public DbSet<Organizador> Organizadores { get; set; }
        public DbSet<Control> Controles { get; set; }
        public DbSet<Cliente> Clientes { get; set; }
        public DbSet<Event> Eventos { get; set; }
        public DbSet<TicketType> TicketTypes { get; set; }
        public DbSet<Ticket> Tickets { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<ReservationItem> ReservationItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Fee> Fees { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<TicketType>()
                .Property(t => t.Precio)
                .HasColumnType("decimal(18,2)"); // Definir precisión y escala

            // Relación: Un Organizador puede tener muchos Eventos
            builder.Entity<Event>()
                .HasOne(e => e.Organizador)
                .WithMany(o => o.Events)
                .HasForeignKey(e => e.OrganizadorId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Un Evento puede tener muchos Tipos de Tickets
            builder.Entity<Event>()
                .HasMany(e => e.TicketTypes)
                .WithOne(tt => tt.Evento)
                .HasForeignKey(tt => tt.EventoId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Un Tipo de Entrada puede tener muchos Tickets
            builder.Entity<TicketType>()
                .HasMany(tt => tt.TicketsVendidos)
                .WithOne(t => t.TicketType)
                .HasForeignKey(t => t.TicketTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Un Cliente puede tener muchos Tickets
            builder.Entity<Cliente>()
                .HasMany(c => c.Tickets)
                .WithOne(t => t.Cliente)
                .HasForeignKey(t => t.ClienteId)
                .OnDelete(DeleteBehavior.Cascade); // Si un Cliente se elimina, sus Tickets también.

            // Relación: Un Control pertenece a un Evento y a un Organizador
            builder.Entity<Control>()
                .HasOne(c => c.Event)
                .WithMany()
                .HasForeignKey(c => c.EventId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Control>()
                .HasOne(c => c.Organizador)
                .WithMany(o => o.ControlAccounts)
                .HasForeignKey(c => c.OrganizadorId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Evento tiene muchos tipos de entrada (TicketType)
            builder.Entity<Event>()
                .HasMany(e => e.TicketTypes)
                .WithOne(tt => tt.Evento)
                .HasForeignKey(tt => tt.EventoId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Un tipo de entrada (TicketType) tiene muchos tickets vendidos
            builder.Entity<TicketType>()
                .HasMany(tt => tt.TicketsVendidos)
                .WithOne(t => t.TicketType)
                .HasForeignKey(t => t.TicketTypeId)
                .OnDelete(DeleteBehavior.Cascade);

            // Unicidad en los correos electrónicos y UserNames
            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.Email)
                .IsUnique();

            builder.Entity<ApplicationUser>()
                .HasIndex(u => u.UserName)
                .IsUnique();

            // Unicidad en el DNI de los clientes
            builder.Entity<Cliente>()
                .HasIndex(c => c.DNI)
                .IsUnique();

            // ==================== NUEVAS RELACIONES PARA SISTEMA DE COMPRAS ====================

            // Configurar precision de decimales para Order
            builder.Entity<Order>()
                .Property(o => o.SubTotal)
                .HasColumnType("decimal(18,2)");

            builder.Entity<Order>()
                .Property(o => o.Fees)
                .HasColumnType("decimal(18,2)");

            builder.Entity<Order>()
                .Property(o => o.Taxes)
                .HasColumnType("decimal(18,2)");

            builder.Entity<Order>()
                .Property(o => o.Total)
                .HasColumnType("decimal(18,2)");

            builder.Entity<Order>()
                .Property(o => o.DiscountAmount)
                .HasColumnType("decimal(18,2)");

            // Índice único para OrderNumber
            builder.Entity<Order>()
                .HasIndex(o => o.OrderNumber)
                .IsUnique();

            // Relación: Un Cliente puede tener muchas Orders
            builder.Entity<Order>()
                .HasOne(o => o.Cliente)
                .WithMany()
                .HasForeignKey(o => o.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Una Order puede tener muchos OrderItems
            builder.Entity<Order>()
                .HasMany(o => o.Items)
                .WithOne(oi => oi.Order)
                .HasForeignKey(oi => oi.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Una Order puede tener muchos Payments
            builder.Entity<Order>()
                .HasMany(o => o.Payments)
                .WithOne(p => p.Order)
                .HasForeignKey(p => p.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Una Order puede tener una Reservation
            builder.Entity<Order>()
                .HasOne(o => o.Reservation)
                .WithOne(r => r.Order)
                .HasForeignKey<Order>(o => o.ReservationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configurar precision de decimales para OrderItem
            builder.Entity<OrderItem>()
                .Property(oi => oi.PrecioUnitario)
                .HasColumnType("decimal(18,2)");

            builder.Entity<OrderItem>()
                .Property(oi => oi.Subtotal)
                .HasColumnType("decimal(18,2)");

            // Relación: Un OrderItem puede tener muchos Tickets
            builder.Entity<OrderItem>()
                .HasMany(oi => oi.Tickets)
                .WithOne(t => t.OrderItem)
                .HasForeignKey(t => t.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Un OrderItem pertenece a un TicketType
            builder.Entity<OrderItem>()
                .HasOne(oi => oi.TicketType)
                .WithMany()
                .HasForeignKey(oi => oi.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Un Cliente puede tener muchas Reservations
            builder.Entity<Reservation>()
                .HasOne(r => r.Cliente)
                .WithMany()
                .HasForeignKey(r => r.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            // Relación: Una Reservation puede tener muchos ReservationItems
            builder.Entity<Reservation>()
                .HasMany(r => r.Items)
                .WithOne(ri => ri.Reservation)
                .HasForeignKey(ri => ri.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            // Relación: Un ReservationItem pertenece a un TicketType
            builder.Entity<ReservationItem>()
                .HasOne(ri => ri.TicketType)
                .WithMany()
                .HasForeignKey(ri => ri.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Configurar precision de decimales para Payment
            builder.Entity<Payment>()
                .Property(p => p.Monto)
                .HasColumnType("decimal(18,2)");

            // Índice en TransactionId para búsquedas rápidas
            builder.Entity<Payment>()
                .HasIndex(p => p.TransactionId);

            // Configurar precision de decimales para Ticket
            builder.Entity<Ticket>()
                .Property(t => t.PrecioFinal)
                .HasColumnType("decimal(18,2)");

            // Índice único para QrCode
            builder.Entity<Ticket>()
                .HasIndex(t => t.QrCode)
                .IsUnique();

            // Configurar precision de decimales para Fee
            builder.Entity<Fee>()
                .Property(f => f.Porcentaje)
                .HasColumnType("decimal(18,4)");

            builder.Entity<Fee>()
                .Property(f => f.MontoFijo)
                .HasColumnType("decimal(18,2)");
        }
    }
}

