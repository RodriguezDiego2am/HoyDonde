using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Sockets;

namespace HoyDonde.API.Models
{
    public class Ticket
    {
        public int Id { get; set; }
        public int TicketTypeId { get; set; } // Relación con el tipo de entrada comprada
        public virtual TicketType TicketType { get; set; } = null!;
        public string ClienteId { get; set; } = string.Empty; // Relación con el cliente dueño del ticket
        public virtual Cliente Cliente { get; set; } = null!;
        public DateTime FechaCompra { get; set; } = DateTime.UtcNow;
    }

}

