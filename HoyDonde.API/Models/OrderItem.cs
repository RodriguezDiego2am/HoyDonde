using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class OrderItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public virtual Order Order { get; set; } = null!;

        public int TicketTypeId { get; set; }
        public virtual TicketType TicketType { get; set; } = null!;

        public int Cantidad { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrecioUnitario { get; set; } // Precio al momento de la compra

        [Column(TypeName = "decimal(18,2)")]
        public decimal Subtotal { get; set; } // Cantidad * PrecioUnitario

        // Tickets generados para este item (se crean después del pago exitoso)
        public virtual List<Ticket> Tickets { get; set; } = new();
    }
}
