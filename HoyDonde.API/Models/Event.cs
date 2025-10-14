using System;
using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    public class Event
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime Fecha { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public EventCategory Categoria { get; set; }
        public int CapacidadMaxima { get; set; }
        public string OrganizadorId { get; set; } = string.Empty;
        public virtual Organizador Organizador { get; set; } = null!;
        public virtual List<TicketType> TicketTypes { get; set; } = new();
        public virtual List<Ticket> Asistentes { get; set; } = new();
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
