using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Filtro de búsqueda pública de eventos (GET /api/events, docs/api-mvp-plan.md §7 Frontend 5).
    // Reemplaza al antiguo filtro de un solo lado (FechaInicio, sin cota superior) por un rango
    // completo FechaDesde/FechaHasta contra Event.FechaInicio: no había otro consumidor de aquel
    // parámetro (ni frontend ni tests lo usaban todavía), así que se migra directamente en vez de
    // mantener dos nombres con significados ambiguos.
    //
    // Ambos valores viajan en UTC y se interpretan tal cual llegan (el emisor —el frontend— es
    // quien resuelve el límite de día local -> UTC, nunca este DTO): FechaDesde es inclusiva
    // (Event.FechaInicio >= FechaDesde), FechaHasta es exclusiva (Event.FechaInicio < FechaHasta).
    //
    // Categoria se tipa como el enum real (no string): así el propio model binding de ASP.NET
    // Core rechaza un valor que no exista en Event.EventCategory con el mismo contrato uniforme
    // VALIDATION_ERROR (ApiBehaviorOptions.InvalidModelStateResponseFactory en Program.cs), sin
    // necesitar un Enum.TryParse manual en el servicio que silenciosamente ignore un filtro mal
    // escrito.
    public class EventSearchFilterDto
    {
        public Event.EventCategory? Categoria { get; set; }
        public string? Ubicacion { get; set; }
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        // Parámetros de paginación para Firebase
        public int Limit { get; set; } = 20;
        public string? LastEventId { get; set; }
    }
}
