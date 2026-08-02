using System;
using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Event
    {
        [Google.Cloud.Firestore.FirestoreDocumentId]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Nombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Ubicacion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaInicio { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaFin { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Descripcion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public EventCategory Categoria { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int CapacidadMaxima { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string OrganizadorPersonaId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public virtual List<TicketType> TicketTypes { get; set; } = new();

        // Asistentes should be a subcollection, not loaded here by default
        public virtual List<Ticket> Asistentes { get; set; } = new();

        [Google.Cloud.Firestore.FirestoreProperty]
        public EventStatus Estado { get; set; } = EventStatus.Borrador;

        // Únicos estados persistidos (docs/api-mvp-plan.md §1). "Finalizado" no se persiste: es un
        // estado efectivo derivado, ver EventEffectiveStatus/GetEstadoEfectivo.
        public enum EventStatus
        {
            Borrador,
            Publicado,
            Cancelado
        }

        // Estado expuesto en lecturas (EventResponse.Estado): igual al persistido salvo que el
        // evento esté Publicado y ya haya pasado FechaFin, en cuyo caso es Finalizado (terminal,
        // docs/api-mvp-plan.md §0.1/§1).
        public enum EventEffectiveStatus
        {
            Borrador,
            Publicado,
            Cancelado,
            Finalizado
        }

        public enum EventCategory
        {
            Musica,
            Deportes,
            Tecnologia,
            Arte,
            Otros
        }

        public EventEffectiveStatus GetEstadoEfectivo(DateTime utcNow)
        {
            if (Estado == EventStatus.Publicado && utcNow > FechaFin)
                return EventEffectiveStatus.Finalizado;

            return Estado switch
            {
                EventStatus.Borrador => EventEffectiveStatus.Borrador,
                EventStatus.Publicado => EventEffectiveStatus.Publicado,
                EventStatus.Cancelado => EventEffectiveStatus.Cancelado,
                _ => throw new InvalidOperationException($"Estado de evento no reconocido: {Estado}")
            };
        }

        // Vigencia de catálogo/detalle público (docs/api-mvp-plan.md §0.1): un evento Publicado
        // sigue siendo visible mientras no haya pasado FechaFin (incluye el evento en curso).
        public bool EsPublicamenteVisible(DateTime utcNow) =>
            Estado == EventStatus.Publicado && utcNow <= FechaFin;

    }

}
