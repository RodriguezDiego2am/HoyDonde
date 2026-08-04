using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Respuesta de GET /api/reports/organizer/sales y GET /api/reports/admin/sales (docs/api-mvp-plan.md
    // §11). Mismo shape para ambos reportes -Organizador y Administrador-: ownership/alcance ya se
    // resolvió del lado de la query, esta respuesta no necesita distinguirlos. FechaDesde/
    // FechaHasta son el rango efectivo aplicado sobre Compra.FechaCompra (nunca Event.FechaInicio).
    public class VentasReporteResponseDto
    {
        public DateTime FechaDesde { get; set; }
        public DateTime FechaHasta { get; set; }
        public string AclaracionImporte { get; set; } = string.Empty;

        public VentasResumenDto Resumen { get; set; } = new();
        public List<VentasSerieBucketDto> SerieTemporal { get; set; } = new();
        public List<VentasTopEventoDto> TopEventos { get; set; } = new();
        public List<VentasCategoriaDto> PorCategoria { get; set; } = new();

        // Vacío salvo que el filtro traiga un eventId (ver VentasTicketTypeDto).
        public List<VentasTicketTypeDto> PorTipoEntrada { get; set; } = new();

        // Opciones reales para poblar selectores de evento/tipo de entrada (ver
        // VentasFiltrosDisponiblesDto): a diferencia de TopEventos (máximo 5), Eventos incluye
        // todos los eventos con Compras en el conjunto ya filtrado por rango/ownership/
        // organizador/categoría, calculado antes de aplicar eventId.
        public VentasFiltrosDisponiblesDto FiltrosDisponibles { get; set; } = new();
    }
}
