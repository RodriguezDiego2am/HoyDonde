using System;
using System.Collections.Generic;
using System.Linq;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    // Agregación pura del reporte de ventas simuladas (docs/api-mvp-plan.md §11): sin FirestoreDb,
    // recibe siempre Compras/Tickets ya leídos y filtrados por el llamador (VentasReporteService),
    // para poder testear fórmulas/redondeo/casos borde con objetos en memoria. Reutiliza el mismo
    // texto de aclaración que el reporte de desempeño (ReporteMetricasCalculator): "importe
    // emitido" nunca es recaudación/cobrado/facturación/ganancia en ningún reporte del MVP.
    public static class VentasMetricasCalculator
    {
        public const string AclaracionImporteFija = ReporteMetricasCalculator.AclaracionImporteFija;

        public static VentasReporteResponseDto Build(
            DateTime fechaDesde,
            DateTime fechaHasta,
            IReadOnlyList<Compra> compras,
            IReadOnlyDictionary<string, List<Ticket>> ticketsPorCompra,
            string? eventIdFiltro)
        {
            return new VentasReporteResponseDto
            {
                FechaDesde = fechaDesde,
                FechaHasta = fechaHasta,
                AclaracionImporte = AclaracionImporteFija,
                Resumen = BuildResumen(compras),
                SerieTemporal = VentasSerieBuilder.Build(compras.ToList(), fechaDesde, fechaHasta),
                TopEventos = BuildTopEventos(compras),
                PorCategoria = BuildPorCategoria(compras),
                PorTipoEntrada = string.IsNullOrEmpty(eventIdFiltro)
                    ? new List<VentasTicketTypeDto>()
                    : BuildPorTipoEntrada(compras, ticketsPorCompra),
            };
        }

        private static VentasResumenDto BuildResumen(IReadOnlyList<Compra> compras)
        {
            var cantidadCompras = compras.Count;
            var entradasEmitidas = compras.Sum(c => c.CantidadEntradas);
            var importeEmitido = Math.Round(compras.Sum(c => c.ImporteTotal), 2);
            var clientesUnicos = compras.Select(c => c.ClientePersonaId).Distinct().Count();

            var porEvento = AgruparPorEvento(compras);

            var mayorImporte = porEvento
                .OrderByDescending(e => e.ImporteEmitido)
                .ThenByDescending(e => e.EntradasEmitidas)
                .ThenBy(e => e.EventoNombre, StringComparer.Ordinal)
                .ThenBy(e => e.EventoId, StringComparer.Ordinal)
                .FirstOrDefault();

            var masEntradas = porEvento
                .OrderByDescending(e => e.EntradasEmitidas)
                .ThenByDescending(e => e.ImporteEmitido)
                .ThenBy(e => e.EventoNombre, StringComparer.Ordinal)
                .ThenBy(e => e.EventoId, StringComparer.Ordinal)
                .FirstOrDefault();

            return new VentasResumenDto
            {
                CantidadCompras = cantidadCompras,
                EntradasEmitidas = entradasEmitidas,
                ImporteEmitido = importeEmitido,
                ImportePromedioPorCompra = cantidadCompras == 0 ? 0m : Math.Round(importeEmitido / cantidadCompras, 2),
                PrecioPromedioEntrada = entradasEmitidas == 0 ? 0m : Math.Round(importeEmitido / entradasEmitidas, 2),
                ClientesUnicos = clientesUnicos,
                EventoConMayorImporte = mayorImporte,
                EventoConMasEntradas = masEntradas,
            };
        }

        private static List<VentasEventoDestacadoDto> AgruparPorEvento(IReadOnlyList<Compra> compras) =>
            compras
                .GroupBy(c => (c.EventoId, c.EventoNombre))
                .Select(g => new VentasEventoDestacadoDto
                {
                    EventoId = g.Key.EventoId,
                    EventoNombre = g.Key.EventoNombre,
                    ImporteEmitido = Math.Round(g.Sum(c => c.ImporteTotal), 2),
                    EntradasEmitidas = g.Sum(c => c.CantidadEntradas),
                })
                .ToList();

        // Máximo 5, orden determinístico (docs/api-mvp-plan.md §11): 1) ImporteEmitido desc,
        // 2) EntradasEmitidas desc, 3) EventoNombre, 4) EventoId como desempate final.
        private static List<VentasTopEventoDto> BuildTopEventos(IReadOnlyList<Compra> compras) =>
            compras
                .GroupBy(c => (c.EventoId, c.EventoNombre))
                .Select(g =>
                {
                    var cantidad = g.Count();
                    var importe = Math.Round(g.Sum(c => c.ImporteTotal), 2);
                    return new VentasTopEventoDto
                    {
                        EventoId = g.Key.EventoId,
                        EventoNombre = g.Key.EventoNombre,
                        CantidadCompras = cantidad,
                        EntradasEmitidas = g.Sum(c => c.CantidadEntradas),
                        ImporteEmitido = importe,
                        ImportePromedioCompra = cantidad == 0 ? 0m : Math.Round(importe / cantidad, 2),
                    };
                })
                .OrderByDescending(e => e.ImporteEmitido)
                .ThenByDescending(e => e.EntradasEmitidas)
                .ThenBy(e => e.EventoNombre, StringComparer.Ordinal)
                .ThenBy(e => e.EventoId, StringComparer.Ordinal)
                .Take(5)
                .ToList();

        private static List<VentasCategoriaDto> BuildPorCategoria(IReadOnlyList<Compra> compras)
        {
            var totalImporte = compras.Sum(c => c.ImporteTotal);

            return compras
                .GroupBy(c => c.Categoria?.ToString() ?? "Sin categoría")
                .Select(g =>
                {
                    var importe = Math.Round(g.Sum(c => c.ImporteTotal), 2);
                    return new VentasCategoriaDto
                    {
                        Categoria = g.Key,
                        CantidadCompras = g.Count(),
                        EntradasEmitidas = g.Sum(c => c.CantidadEntradas),
                        ImporteEmitido = importe,
                        PorcentajeDelImporteTotal = totalImporte <= 0 ? 0 : Math.Round((double)(g.Sum(c => c.ImporteTotal) / totalImporte) * 100, 2),
                    };
                })
                .OrderByDescending(c => c.ImporteEmitido)
                .ThenBy(c => c.Categoria, StringComparer.Ordinal)
                .ToList();
        }

        // Solo se llama cuando hay un eventId filtrado (ver VentasReporteResponseDto.PorTipoEntrada):
        // tickets legacy sin CompraId nunca llegan acá porque ticketsPorCompra solo indexa lotes de
        // Ticket leídos por WhereIn(CompraId) contra las Compras ya seleccionadas.
        private static List<VentasTicketTypeDto> BuildPorTipoEntrada(
            IReadOnlyList<Compra> compras,
            IReadOnlyDictionary<string, List<Ticket>> ticketsPorCompra)
        {
            var compraIds = compras.Select(c => c.Id).ToHashSet();
            var tickets = ticketsPorCompra
                .Where(kv => compraIds.Contains(kv.Key))
                .SelectMany(kv => kv.Value)
                .ToList();

            var totalImporte = tickets.Sum(t => t.PrecioPagado);

            return tickets
                .GroupBy(t => (t.TicketTypeId, t.TicketTypeNombre))
                .Select(g =>
                {
                    var importe = Math.Round(g.Sum(t => t.PrecioPagado), 2);
                    return new VentasTicketTypeDto
                    {
                        TicketTypeId = g.Key.TicketTypeId,
                        TicketTypeNombre = g.Key.TicketTypeNombre,
                        CantidadComprasDistintas = g.Select(t => t.CompraId).Where(id => !string.IsNullOrEmpty(id)).Distinct().Count(),
                        EntradasEmitidas = g.Count(),
                        ImporteEmitido = importe,
                        PorcentajeDelImporteTotal = totalImporte <= 0 ? 0 : Math.Round((double)(g.Sum(t => t.PrecioPagado) / totalImporte) * 100, 2),
                    };
                })
                .OrderByDescending(t => t.ImporteEmitido)
                .ThenBy(t => t.TicketTypeNombre, StringComparer.Ordinal)
                .ToList();
        }
    }
}
