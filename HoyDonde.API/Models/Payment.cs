using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class Payment
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public virtual Order Order { get; set; } = null!;

        [Column(TypeName = "decimal(18,2)")]
        public decimal Monto { get; set; }

        public PaymentMethod MetodoPago { get; set; }
        public PaymentStatus Estado { get; set; } = PaymentStatus.Pending;

        public DateTime FechaCreacion { get; set; } = DateTime.UtcNow;
        public DateTime? FechaProcesado { get; set; }

        // Datos de la pasarela de pago
        public string? TransactionId { get; set; } // ID de transacción de la pasarela
        public string? PaymentGateway { get; set; } // MercadoPago, Stripe, etc.
        public string? PaymentGatewayResponse { get; set; } // JSON con respuesta completa

        // Tokenización de métodos de pago
        public string? PaymentMethodToken { get; set; } // Token del método de pago guardado

        public string? ErrorMessage { get; set; } // Mensaje de error si falla
    }

    public enum PaymentMethod
    {
        CreditCard,
        DebitCard,
        BankTransfer,
        MercadoPago,
        Cash,
        Other
    }

    public enum PaymentStatus
    {
        Pending,      // Pendiente de procesar
        Processing,   // En proceso
        Approved,     // Aprobado por la pasarela
        Completed,    // Completado exitosamente
        Failed,       // Fallido
        Cancelled,    // Cancelado
        Refunded      // Reembolsado
    }
}
