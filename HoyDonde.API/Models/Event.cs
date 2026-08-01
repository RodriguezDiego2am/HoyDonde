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
        public DateTime Fecha { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Descripcion { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public EventCategory Categoria { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public int CapacidadMaxima { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public string OrganizadorId { get; set; } = string.Empty;

        // Navigation properties are removed or ignored in Firestore
        public virtual Organizador Organizador { get; set; } = null!;
        
        [Google.Cloud.Firestore.FirestoreProperty]
        public virtual List<TicketType> TicketTypes { get; set; } = new();

        // Asistentes should be a subcollection, not loaded here by default
        public virtual List<Ticket> Asistentes { get; set; } = new();

        [Google.Cloud.Firestore.FirestoreProperty]
        public EventStatus Estado { get; set; } = EventStatus.Activo;
        public enum EventStatus
        {
            Activo,
            Pendiente,
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

    }

}
