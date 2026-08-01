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

        [Google.Cloud.Firestore.FirestoreProperty]
        public string TicketTypeId { get; set; } = string.Empty;
        
        public virtual TicketType TicketType { get; set; } = null!;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string ClienteId { get; set; } = string.Empty; 
        
        public virtual Cliente Cliente { get; set; } = null!;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime FechaCompra { get; set; } = DateTime.UtcNow;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventoId { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public TicketStatus Estado { get; set; } = TicketStatus.Emitido;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? FechaUso { get; set; }

        // UID del Control (Firebase) que realizó la validación. Nunca se acepta desde el cliente.
        [Google.Cloud.Firestore.FirestoreProperty]
        public string ValidadoPor { get; set; } = string.Empty;

        public enum TicketStatus
        {
            Emitido,
            Usado,
            Anulado
        }
    }

}

