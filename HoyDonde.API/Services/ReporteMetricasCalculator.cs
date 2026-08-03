using System;
using System.Collections.Generic;
using System.Linq;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // Agregación pura del reporte de eventos (docs/api-mvp-plan.md §11.9): sin FirestoreDb ni
    // ninguna otra dependencia externa, para poder testear fórmulas/redondeo/casos borde con
    // objetos en memoria, sin Firestore Emulator. ReporteService es responsable de ownership,
    // filtros y lecturas de Firestore; esta clase solo calcula sobre el Event/Ticket ya acotados.
    public static class ReporteMetricasCalculator
    {
        public const string AclaracionImporteFija =
            "El MVP no procesa pagos reales: \"importe emitido\" es la suma de los precios fotografiados en cada ticket al comprar, nunca una recaudación ni un cobro real.";

        public static ReporteEventosResponseDto Build(
            DateTime fechaDesde,
            DateTime fechaHasta,
            IReadOnlyList<Event> eventos,
            IReadOnlyDictionary<string, List<Ticket>> ticketsPorEvento,
            string? ticketTypeId,
            DateTime utcNow)
        {
            var detalles = eventos
                .Select(evento => BuildEventoDetalle(
                    evento,
                    ticketsPorEvento.TryGetValue(evento.Id, out var tickets) ? tickets : new List<Ticket>(),
                    ticketTypeId,
                    utcNow))
                .ToList();

            return new ReporteEventosResponseDto
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                AclaracionImporte = AclaracionImporteFija,
                Resumen = BuildResumen(detalles),
                Eventos = detalles,
            };
        }

        private static ReporteEventoDetalleDto BuildEventoDetalle(Event evento, List<Ticket> ticketsDelEvento, string? ticketTypeId, DateTime utcNow)
        {
            var todosLosTipos = evento.TicketTypes ?? new List<TicketType>();

            // Cuando el filtro trae ticketTypeId, toda la fila del evento se acota únicamente a
            // ese tipo (docs/api-mvp-plan.md §11.2): capacidad, stock, métricas e importe, y el
            // desglose por tipo colapsa a una sola fila.
            var tiposRelevantes = string.IsNullOrEmpty(ticketTypeId)
                ? todosLosTipos
                : todosLosTipos.Where(t => t.Id == ticketTypeId).ToList();

            var tiposDeEntrada = tiposRelevantes
                .Select(tt => BuildTicketTypeDetalle(tt, ticketsDelEvento.Where(t => t.TicketTypeId == tt.Id).ToList()))
                .ToList();

            var ticketsRelevantes = string.IsNullOrEmpty(ticketTypeId)
                ? ticketsDelEvento
                : ticketsDelEvento.Where(t => t.TicketTypeId == ticketTypeId).ToList();

            var capacidadInicial = string.IsNullOrEmpty(ticketTypeId)
                ? evento.CapacidadMaxima
                : tiposDeEntrada.Sum(t => t.CapacidadInicial);

            var stockDisponible = tiposDeEntrada.Sum(t => t.StockDisponible);
            var metricas = CalcularMetricas(ticketsRelevantes, capacidadInicial);

            return new ReporteEventoDetalleDto
            {
                EventId = evento.Id,
                Nombre = evento.Nombre,
                Ubicacion = evento.Ubicacion,
                Categoria = evento.Categoria.ToString(),
                Estado = evento.GetEstadoEfectivo(utcNow).ToString(),
                FechaInicio = evento.FechaInicio,
                FechaFin = evento.FechaFin,
                CapacidadInicial = capacidadInicial,
                StockDisponible = stockDisponible,
                EntradasEmitidas = metricas.Emitidas,
                EntradasUsadas = metricas.Usadas,
                EntradasAnuladas = metricas.Anuladas,
                EntradasPendientes = metricas.Pendientes,
                PorcentajeOcupacion = metricas.PorcentajeOcupacion,
                PorcentajeAsistencia = metricas.PorcentajeAsistencia,
                PorcentajeUtilizacion = metricas.PorcentajeUtilizacion,
                ImporteEmitido = metricas.ImporteEmitido,
                TiposDeEntrada = tiposDeEntrada,
            };
        }

        private static ReporteTicketTypeDetalleDto BuildTicketTypeDetalle(TicketType tipo, List<Ticket> ticketsDelTipo)
        {
            // Capacidad derivada (docs/api-mvp-plan.md §11.2): stock actual + entradas ya emitidas
            // de ese tipo. Es matemáticamente correcta bajo el código actual (sin reposición de
            // stock), pero es una derivación, no un dato persistido: no existe capacidad inicial
            // por TicketType.
            var capacidadInicial = tipo.CantidadDisponible + ticketsDelTipo.Count;
            var metricas = CalcularMetricas(ticketsDelTipo, capacidadInicial);

            return new ReporteTicketTypeDetalleDto
            {
                TicketTypeId = tipo.Id,
                Nombre = tipo.Nombre,
                CapacidadInicial = capacidadInicial,
                StockDisponible = tipo.CantidadDisponible,
                EntradasEmitidas = metricas.Emitidas,
                EntradasUsadas = metricas.Usadas,
                EntradasAnuladas = metricas.Anuladas,
                EntradasPendientes = metricas.Pendientes,
                PorcentajeOcupacion = metricas.PorcentajeOcupacion,
                PorcentajeAsistencia = metricas.PorcentajeAsistencia,
                PorcentajeUtilizacion = metricas.PorcentajeUtilizacion,
                ImporteEmitido = metricas.ImporteEmitido,
            };
        }

        private static ReporteResumenDto BuildResumen(List<ReporteEventoDetalleDto> detalles)
        {
            var capacidadInicial = detalles.Sum(d => d.CapacidadInicial);
            var stockDisponible = detalles.Sum(d => d.StockDisponible);
            var emitidas = detalles.Sum(d => d.EntradasEmitidas);
            var usadas = detalles.Sum(d => d.EntradasUsadas);
            var anuladas = detalles.Sum(d => d.EntradasAnuladas);
            var pendientes = detalles.Sum(d => d.EntradasPendientes);
            var importe = detalles.Sum(d => d.ImporteEmitido);

            return new ReporteResumenDto
            {
                CantidadEventos = detalles.Count,
                CapacidadInicial = capacidadInicial,
                StockDisponible = stockDisponible,
                EntradasEmitidas = emitidas,
                EntradasUsadas = usadas,
                EntradasAnuladas = anuladas,
                EntradasPendientes = pendientes,
                PorcentajeOcupacion = PorcentajeSeguro(emitidas, capacidadInicial),
                PorcentajeAsistencia = PorcentajeSeguro(usadas, emitidas),
                PorcentajeUtilizacion = PorcentajeSeguro(usadas, capacidadInicial),
                ImporteEmitido = Math.Round(importe, 2),
            };
        }

        private static (int Emitidas, int Usadas, int Anuladas, int Pendientes, double PorcentajeOcupacion, double PorcentajeAsistencia, double PorcentajeUtilizacion, decimal ImporteEmitido)
            CalcularMetricas(List<Ticket> tickets, int capacidadInicial)
        {
            // Fórmulas fijas (docs/api-mvp-plan.md §11.9): Emitidas = COUNT(Ticket) del conjunto ya
            // acotado (nunca "reservado": todo Ticket existente ya se emitió). Estado histórico
            // persistido, nunca Utilizable/MotivoNoUtilizable (esos son derivados para la UX del
            // Cliente, no para este reporte).
            var emitidas = tickets.Count;
            var usadas = tickets.Count(t => t.Estado == Ticket.TicketStatus.Usado);
            var anuladas = tickets.Count(t => t.Estado == Ticket.TicketStatus.Anulado);
            var pendientes = tickets.Count(t => t.Estado == Ticket.TicketStatus.Emitido);
            var importe = Math.Round(tickets.Sum(t => t.PrecioPagado), 2);

            return (
                emitidas, usadas, anuladas, pendientes,
                PorcentajeSeguro(emitidas, capacidadInicial),
                PorcentajeSeguro(usadas, emitidas),
                PorcentajeSeguro(usadas, capacidadInicial),
                importe);
        }

        // División por cero -> 0. Redondeo explícito a 2 decimales, expresado como porcentaje
        // (0-100), nunca como fracción 0-1.
        private static double PorcentajeSeguro(int numerador, int denominador) =>
            denominador <= 0 ? 0 : Math.Round((double)numerador / denominador * 100, 2);
    }
}
