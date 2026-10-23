using System;

namespace HoyDonde.API.DTOs
{
    public class TicketResponseDto
    {
        public string Id { get; set; } = string.Empty;

        // Nulo para tickets emitidos antes de la Etapa "Compra" (docs/api-mvp-plan.md §14), y
        // también para quien recibió el ticket por transferencia (la Compra es del comprador).
        public string? CompraId { get; set; }

        public string EventoId { get; set; } = string.Empty;
        public string TicketTypeId { get; set; } = string.Empty;
        public string ClientePersonaId { get; set; } = string.Empty;
        public DateTime FechaCompra { get; set; }

        // Estado histórico/persistido del ticket (Emitido/Usado/Anulado); nunca reescrito por
        // una cancelación de evento (docs/api-mvp-plan.md §3).
        public string Estado { get; set; } = string.Empty;

        // Derivado en el momento de la lectura, sin mutar el documento: false si el ticket ya
        // fue usado/anulado, o si el evento fue cancelado o ya está Finalizado (derivado).
        public bool Utilizable { get; set; }

        // Nulo si Utilizable == true; en caso contrario uno de: "Usado", "Anulado",
        // "EventoCancelado", "EventoFinalizado".
        public string? MotivoNoUtilizable { get; set; }

        // Fotografía inmutable tomada en el momento de la compra (copiada del Event leído dentro
        // de la transacción de compra); nunca se recalcula contra el Event actual.
        public string EventoNombre { get; set; } = string.Empty;
        public string TicketTypeNombre { get; set; } = string.Empty;
        // Null when the caller received the ticket through a transfer: the original price is private
        // to the purchaser.
        public decimal? PrecioPagado { get; set; }
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }

        // True only for explicitly identified historical simulated purchases.
        public bool PagoSimulado { get; set; }

        // Opaque value the client renders as the ticket QR. Only present for the ticket's current
        // owner while Utilizable == true; rotates on every transfer. Treat as a secret: never log it.
        public string? QrToken { get; set; }
    }
}
