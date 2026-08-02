using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Respuesta acotada de GET /api/events/control/me (docs/api-mvp-plan.md §6): los eventos a
    // los que el Control autenticado fue asignado, en cualquier estado persistido/efectivo (la
    // UI decide qué habilitar; la validación sigue aplicando sus propias reglas de vigencia).
    // Nunca expone tipos de ticket, precios ni stock.
    public class EventoAsignadoResponseDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public Event.EventEffectiveStatus Estado { get; set; }
    }
}
