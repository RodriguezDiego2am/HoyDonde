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
        }
    }
}

