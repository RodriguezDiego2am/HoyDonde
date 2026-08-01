using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Campos editables por el organizador dueño del evento.
    // Deliberadamente no incluye Estado ni OrganizadorId: el ciclo de estados
    // y la propiedad del evento no se tocan en este hotfix.
    public class EventUpdateRequest
    {
        public string Nombre { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public string Ubicacion { get; set; } = string.Empty;
        public Event.EventCategory Categoria { get; set; }
    }
}
