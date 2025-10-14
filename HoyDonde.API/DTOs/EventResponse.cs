using System;
using System.Collections.Generic;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    public class EventResponse
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public string Ubicacion { get; set; } = string.Empty;
        public Event.EventCategory Categoria { get; set; }
        public Event.EventStatus Estado { get; set; }
        public List<TicketGroupDto> TicketGroups { get; set; } = new();
    }
}
