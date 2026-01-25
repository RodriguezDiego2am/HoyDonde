using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class Ticket
    {
        public int Id { get; set; }

        public int TicketTypeId { get; set; }
        public virtual TicketType TicketType { get; set; } = null!;

        public string ClienteId { get; set; } = string.Empty;
        public virtual Cliente Cliente { get; set; } = null!;

        public int? OrderItemId { get; set; }
        public virtual OrderItem? OrderItem { get; set; }

        public DateTime FechaCompra { get; set; } = DateTime.UtcNow;

        // QR Code
        public string QrCode { get; set; } = string.Empty; // Código QR único
        public string QrCodeHash { get; set; } = string.Empty; // Hash del QR para verificación

        // Estado del ticket
        public TicketStatus Estado { get; set; } = TicketStatus.Valid;

        // Validación
        public bool EsUsado { get; set; } = false;
        public DateTime? FechaUso { get; set; }
        public string? UsuarioValidadorId { get; set; } // ID del usuario Control que validó

        // Información adicional
        public bool EsCortesia { get; set; } = false; // Si es entrada de cortesía

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioFinal { get; set; } = 0; // Precio pagado (puede ser 0 si es cortesía)
    }

    public enum TicketStatus
    {
        Valid,        // Válido, no usado
        Used,         // Usado (ya ingresó al evento)
        Cancelled,    // Cancelado/reembolsado
        Expired       // Expirado (evento pasado)
    }
}

