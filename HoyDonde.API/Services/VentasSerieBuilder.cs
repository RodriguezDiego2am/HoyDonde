using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public enum VentasSerieGranularidad
    {
        Diaria,
        Semanal,
        Mensual,
    }

    // Serie temporal continua del reporte de ventas (docs/api-mvp-plan.md §11): agrupa por día,
    // semana (desde el lunes) o mes según el largo del rango, siempre en la zona horaria funcional
    // de HoyDonde (ArgentinaTimeZoneProvider), incluyendo períodos con cero. Clase pura -sin
    // FirestoreDb- para poder testear los buckets/etiquetas con objetos en memoria.
    public static class VentasSerieBuilder
    {
        private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

        public static VentasSerieGranularidad DetermineGranularidad(TimeSpan rango)
        {
            if (rango.TotalDays <= 31) return VentasSerieGranularidad.Diaria;
            if (rango.TotalDays <= 180) return VentasSerieGranularidad.Semanal;
            return VentasSerieGranularidad.Mensual;
        }

        public static List<VentasSerieBucketDto> Build(IReadOnlyList<Compra> compras, DateTime fechaDesdeUtc, DateTime fechaHastaUtc)
        {
            var granularidad = DetermineGranularidad(fechaHastaUtc - fechaDesdeUtc);
            var localDesde = ArgentinaTimeZoneProvider.ToLocal(fechaDesdeUtc);
            var localHastaExclusive = ArgentinaTimeZoneProvider.ToLocal(fechaHastaUtc);

            var limites = new List<(DateTime Start, DateTime End)>();
            var cursor = FloorToGranularidad(localDesde, granularidad);
            while (cursor < localHastaExclusive)
            {
                var siguiente = StepGranularidad(cursor, granularidad);
                limites.Add((cursor, siguiente));
                cursor = siguiente;
            }

            var buckets = limites.Select(l => new VentasSerieBucketDto
            {
                PeriodoDesde = ArgentinaTimeZoneProvider.ToUtc(l.Start),
                PeriodoHasta = ArgentinaTimeZoneProvider.ToUtc(l.End),
                Etiqueta = BuildEtiqueta(l.Start, l.End, granularidad),
                CantidadCompras = 0,
                EntradasEmitidas = 0,
                ImporteEmitido = 0m,
            }).ToList();

            var indicePorInicio = limites
                .Select((l, idx) => (l.Start, idx))
                .ToDictionary(x => x.Start, x => x.idx);

            foreach (var compra in compras)
            {
                var localFecha = ArgentinaTimeZoneProvider.ToLocal(compra.FechaCompra);
                var bucketStart = FloorToGranularidad(localFecha, granularidad);

                // Defensivo: toda Compra ya viene acotada a [fechaDesdeUtc, fechaHastaUtc) por la
                // query del llamador, así que su fecha local siempre cae en [localDesde,
                // localHastaExclusive) y por lo tanto en uno de los buckets generados arriba.
                if (!indicePorInicio.TryGetValue(bucketStart, out var idx)) continue;

                var bucket = buckets[idx];
                bucket.CantidadCompras++;
                bucket.EntradasEmitidas += compra.CantidadEntradas;
                bucket.ImporteEmitido += compra.ImporteTotal;
            }

            foreach (var bucket in buckets)
            {
                bucket.ImporteEmitido = Math.Round(bucket.ImporteEmitido, 2);
            }

            return buckets;
        }

        private static DateTime FloorToGranularidad(DateTime local, VentasSerieGranularidad g) => g switch
        {
            VentasSerieGranularidad.Diaria => local.Date,
            VentasSerieGranularidad.Semanal => local.Date.AddDays(-DiasDesdeLunes(local.DayOfWeek)),
            VentasSerieGranularidad.Mensual => new DateTime(local.Year, local.Month, 1),
            _ => local.Date,
        };

        private static DateTime StepGranularidad(DateTime start, VentasSerieGranularidad g) => g switch
        {
            VentasSerieGranularidad.Diaria => start.AddDays(1),
            VentasSerieGranularidad.Semanal => start.AddDays(7),
            VentasSerieGranularidad.Mensual => start.AddMonths(1),
            _ => start.AddDays(1),
        };

        // DayOfWeek: Sunday=0..Saturday=6. Lunes debe mapear a 0 días de offset.
        private static int DiasDesdeLunes(DayOfWeek dow) => ((int)dow + 6) % 7;

        private static string BuildEtiqueta(DateTime start, DateTime endExclusive, VentasSerieGranularidad g) => g switch
        {
            VentasSerieGranularidad.Diaria => start.ToString("dd/MM", EsAr),
            VentasSerieGranularidad.Semanal => $"{start:dd/MM} – {endExclusive.AddDays(-1):dd/MM}",
            VentasSerieGranularidad.Mensual => CapitalizeFirst(start.ToString("MMM yyyy", EsAr)),
            _ => start.ToString("dd/MM", EsAr),
        };

        private static string CapitalizeFirst(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
