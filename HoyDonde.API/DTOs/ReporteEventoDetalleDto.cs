using System;
using System.Collections.Generic;

namespace HoyDonde.API.DTOs
{
    // Detalle de un evento propio dentro del reporte (docs/api-mvp-plan.md §11.9). Nunca expone
    // OrganizadorPersonaId, UID de Firebase, UsuarioId ni ExternalSubjectId: el organizador ya es
    // el actor autenticado, no hace falta repetirlo. Estado es el efectivo (incluye Finalizado,
    // igual que EventResponse.Estado), calculado con el mismo utcNow que el resto del reporte.
    public class ReporteEventoDetalleDto
    {
        public string EventId { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Ubicacion { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Estado { get; set; } = string.Empty;
        public DateTime FechaInicio { get; set; }
        public DateTime FechaFin { get; set; }
        public int CapacidadInicial { get; set; }
        public int StockDisponible { get; set; }
        public int EntradasEmitidas { get; set; }
        public int EntradasUsadas { get; set; }
        public int EntradasAnuladas { get; set; }
        public int EntradasPendientes { get; set; }
        public double PorcentajeOcupacion { get; set; }
        public double PorcentajeAsistencia { get; set; }
        public double PorcentajeUtilizacion { get; set; }
        public decimal ImporteEmitido { get; set; }

        // Entradas emitidas nunca validadas (docs/api-mvp-plan.md §11): null/no aplicable salvo
        // que el evento esté efectivamente Finalizado -nunca se llama "ausentismo" a entradas
        // pendientes de un evento futuro o en curso, que simplemente no tienen este dato-.
        public int? EntradasNoUtilizadas { get; set; }
        public double? PorcentajeNoUtilizacion { get; set; }

        // Colapsa a un único elemento (el tipo filtrado) cuando el filtro trae ticketTypeId.
        public List<ReporteTicketTypeDetalleDto> TiposDeEntrada { get; set; } = new();
    }
}
