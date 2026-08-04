using System;

namespace HoyDonde.API.Models
{
    // Agrupa los Ticket emitidos por una misma operación de compra (docs/api-mvp-plan.md §3/§14):
    // Persona (Cliente) 1─* Compra 1─* Ticket, Evento 1─* Compra. La relación hacia Ticket se
    // representa exclusivamente vía Ticket.CompraId — Compra nunca guarda una lista de TicketIds.
    [Google.Cloud.Firestore.FirestoreData]
    public class Compra
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ClientePersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaCompra { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int CantidadEntradas { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty(ConverterType = typeof(HoyDonde.API.Converters.DecimalFirestoreConverter))]
        public decimal ImporteTotal { get; set; }

        // Siempre true en el MVP: el pago simulado es el único mecanismo existente.
        [Google.Cloud.Firestore.FirestoreProperty]
        public bool PagoSimulado { get; set; } = true;

        // Fotografía inmutable tomada del Event dentro de la misma transacción de compra (mismo
        // criterio que Ticket.EventoNombre/FechaInicio/FechaFin): nunca se recalcula después.
        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoNombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Ubicacion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaInicio { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaFin { get; set; }
    }
}
