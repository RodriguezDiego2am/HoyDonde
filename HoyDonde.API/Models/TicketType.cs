using System;
using System.Collections.Generic;
using HoyDonde.API.Models;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class TicketType
    {
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Nombre { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty(ConverterType = typeof(HoyDonde.API.Converters.DecimalFirestoreConverter))]
        public decimal Precio { get; set; }

        public string EventoId { get; set; } = string.Empty; 
        
        public virtual Event Evento { get; set; } = null!;
        
        [Google.Cloud.Firestore.FirestoreProperty]
        public int CantidadDisponible { get; set; }
        
        public virtual List<Ticket> TicketsVendidos { get; set; } = new();
    }
}
