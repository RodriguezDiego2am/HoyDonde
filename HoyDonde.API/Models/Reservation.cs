using System;
using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    public class Reservation
    {
        public int Id { get; set; }
        public string ClienteId { get; set; } = string.Empty;
        public virtual Cliente Cliente { get; set; } = null!;

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime FechaExpiracion { get; set; } // Fecha en que expira la reserva

        public ReservationStatus Estado { get; set; } = ReservationStatus.Active;

        public virtual List<ReservationItem> Items { get; set; } = new();

        public int? OrderId { get; set; }
        public virtual Order? Order { get; set; }
    }

    public enum ReservationStatus
    {
        Active,      // Reserva activa, bloqueando stock
        Completed,   // Reserva completada (orden pagada)
        Expired,     // Reserva expirada, stock liberado
        Cancelled    // Reserva cancelada manualmente
    }
}
