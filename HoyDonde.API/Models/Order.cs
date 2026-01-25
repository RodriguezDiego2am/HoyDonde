using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class Order
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty; // Número único de orden (ej: ORD-20260125-001)
        public string ClienteId { get; set; } = string.Empty;
        public virtual Cliente Cliente { get; set; } = null!;
        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime FechaActualizacion { get; set; } = DateTime.UtcNow;
        public OrderStatus Estado { get; set; } = OrderStatus.Pending;

        [Column(TypeName = "decimal(18,2)")]
        public decimal SubTotal { get; set; } = 0; // Suma de precios de items

        [Column(TypeName = "decimal(18,2)")]
        public decimal Fees { get; set; } = 0; // Comisiones de servicio

        [Column(TypeName = "decimal(18,2)")]
        public decimal Taxes { get; set; } = 0; // Impuestos

        [Column(TypeName = "decimal(18,2)")]
        public decimal Total { get; set; } = 0; // SubTotal + Fees + Taxes

        public virtual List<OrderItem> Items { get; set; } = new();
        public virtual List<Payment> Payments { get; set; } = new();

        public int? ReservationId { get; set; }
        public virtual Reservation? Reservation { get; set; }

        public string? CouponCode { get; set; } // Código de cupón aplicado

        [Column(TypeName = "decimal(18,2)")]
        public decimal DiscountAmount { get; set; } = 0; // Descuento aplicado
    }

    public enum OrderStatus
    {
        Pending,      // Orden creada, pendiente de pago
        Processing,   // Pago en proceso
        Paid,         // Pagado exitosamente
        Failed,       // Pago fallido
        Cancelled,    // Cancelado por el usuario
        Expired,      // Expirado por tiempo
        Refunded      // Reembolsado
    }
}
