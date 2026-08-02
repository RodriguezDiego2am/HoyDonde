using System;
using System.Collections.Generic;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    public class EventResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public string Ubicacion { get; set; } = string.Empty;
        public Event.EventCategory Categoria { get; set; }

        // Estado efectivo/derivado (docs/api-mvp-plan.md §2): puede valer Finalizado aunque el
        // valor persistido siga siendo Publicado.
        public Event.EventEffectiveStatus Estado { get; set; }

        public List<TicketGroupDto> TicketGroups { get; set; } = new();
    }
}
