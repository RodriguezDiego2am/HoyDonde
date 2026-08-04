using System;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Filtro de GET /api/reports/admin/sales (docs/api-mvp-plan.md §11): filtra por
    // Compra.FechaCompra, igual criterio que VentasOrganizerFilterDto. OrganizadorPersonaId es
    // opcional y arbitrario -solo Admin-, mismo criterio que ReporteAdminEventosFilterDto. Sin
    // TicketTypeId (no forma parte de los filtros del Administrador).
    public class VentasAdminFilterDto
    {
        public DateTime? FechaDesde { get; set; }
        public DateTime? FechaHasta { get; set; }

        public string? OrganizadorPersonaId { get; set; }
        public string? EventId { get; set; }
        public Event.EventCategory? Categoria { get; set; }
    }
}
