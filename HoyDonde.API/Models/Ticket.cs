using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net.Sockets;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Ticket
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        // Obligatorio para todo ticket nuevo (siempre seteado por TicketService.BuyTicketsAsync,
        // dentro de la misma transacción que crea la Compra). Nullable únicamente por
        // compatibilidad con tickets emitidos antes de esta etapa, que no tienen Compra asociada
        // y no reciben una retroactivamente (docs/api-mvp-plan.md §14) — se siguen leyendo y
        // mostrando igual, solo que sin CompraId.
        [Google.Cloud.Firestore.FirestoreProperty]
        public string? CompraId { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketTypeId { get; set; } = string.Empty;

        public virtual TicketType TicketType { get; set; } = null!;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ClientePersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaCompra { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public TicketStatus Estado { get; set; } = TicketStatus.Emitido;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? FechaUso { get; set; }

        // PersonaId del Control que realizó la validación. Nunca se acepta desde el cliente.
        [Google.Cloud.Firestore.FirestoreProperty]
        public string ValidadoPorPersonaId { get; set; } = string.Empty;

        // Fotografía inmutable tomada en el momento de la compra (docs/api-mvp-plan.md §3): nunca
        // se recalcula después, ni siquiera si el Event/TicketType cambiaran (un evento publicado
        // ya es inmutable, pero esto además blinda contra cualquier futura excepción a esa regla).
        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoNombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketTypeNombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty(ConverterType = typeof(HoyDonde.API.Converters.DecimalFirestoreConverter))]
        public decimal PrecioPagado { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaInicio { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaFin { get; set; }

        public enum TicketStatus
        {
            Emitido,
            Usado,
            Anulado
        }
    }

}

