using System;
using System.Collections.Generic;
using System.Linq;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Serie temporal del reporte de ventas (docs/api-mvp-plan.md §11): granularidad por largo de
    // rango, semanas desde el lunes, zona horaria Argentina, continuidad (buckets con cero) y que
    // la suma de la serie coincida exactamente con la suma de las Compras de entrada. Clase pura
    // -sin Firestore-, ver Integration/VentasReporteServiceEmulatorTests para el recorrido real.
    public class VentasSerieBuilderTests
    {
        private static Compra BuildCompra(DateTime fechaCompraUtc, int cantidad = 1, decimal importe = 100m, string eventoId = "event-1") => new()
        {
            Id = Guid.NewGuid().ToString(),
            ClientePersonaId = $"persona-{Guid.NewGuid():N}",
            EventoId = eventoId,
            FechaCompra = fechaCompraUtc,
            CantidadEntradas = cantidad,
            ImporteTotal = importe,
        };

        [Theory]
        [InlineData(1, VentasSerieGranularidad.Diaria)]
        [InlineData(31, VentasSerieGranularidad.Diaria)]
        [InlineData(32, VentasSerieGranularidad.Semanal)]
        [InlineData(180, VentasSerieGranularidad.Semanal)]
        [InlineData(181, VentasSerieGranularidad.Mensual)]
        [InlineData(366, VentasSerieGranularidad.Mensual)]
        public void DetermineGranularidad_UsesExpectedThresholds(int dias, VentasSerieGranularidad esperado)
        {
            var resultado = VentasSerieBuilder.DetermineGranularidad(TimeSpan.FromDays(dias));
            Assert.Equal(esperado, resultado);
        }

        [Fact]
        public void Build_RangoDiario_GeneraUnBucketPorDia_IncluyendoCero()
        {
            // Rango de 3 días en UTC-3 (Argentina, sin DST): 05:00Z 1/6 = 02:00 local 1/6.
            var desde = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
            var hasta = new DateTime(2026, 6, 4, 3, 0, 0, DateTimeKind.Utc);
            var compras = new List<Compra> { BuildCompra(new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc)) };

            var serie = VentasSerieBuilder.Build(compras, desde, hasta);

            Assert.Equal(3, serie.Count);
            Assert.Equal(0, serie[0].CantidadCompras);
            Assert.Equal(1, serie[1].CantidadCompras); // día con la compra
            Assert.Equal(0, serie[2].CantidadCompras);
        }

        [Fact]
        public void Build_Semanal_BucketsStartOnMonday()
        {
            // 2026-06-01 es lunes en el calendario real.
            var desde = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc); // 00:00 local lunes
            var hasta = desde.AddDays(35);

            var serie = VentasSerieBuilder.Build(new List<Compra>(), desde, hasta);

            Assert.All(serie, bucket =>
            {
                var localStart = ArgentinaTimeZoneProvider.ToLocal(bucket.PeriodoDesde);
                Assert.Equal(DayOfWeek.Monday, localStart.DayOfWeek);
            });
        }

        [Fact]
        public void Build_SumaDeBuckets_CoincideExactamenteConElTotalDeEntrada()
        {
            var desde = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
            var hasta = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc); // ~151 días -> semanal
            var compras = new List<Compra>
            {
                BuildCompra(new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc), cantidad: 2, importe: 50m),
                BuildCompra(new DateTime(2026, 2, 20, 10, 0, 0, DateTimeKind.Utc), cantidad: 3, importe: 75.555m),
                BuildCompra(new DateTime(2026, 5, 31, 23, 0, 0, DateTimeKind.Utc), cantidad: 1, importe: 10m),
            };

            var serie = VentasSerieBuilder.Build(compras, desde, hasta);

            Assert.Equal(compras.Count, serie.Sum(b => b.CantidadCompras));
            Assert.Equal(compras.Sum(c => c.CantidadEntradas), serie.Sum(b => b.EntradasEmitidas));
            Assert.Equal(Math.Round(compras.Sum(c => c.ImporteTotal), 2), serie.Sum(b => b.ImporteEmitido));
        }

        [Fact]
        public void Build_RangoMensual_AgrupaPorMesCalendario()
        {
            var desde = new DateTime(2026, 1, 1, 3, 0, 0, DateTimeKind.Utc);
            var hasta = new DateTime(2027, 1, 1, 3, 0, 0, DateTimeKind.Utc); // 365 días -> mensual
            var compras = new List<Compra>
            {
                BuildCompra(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc)),
                BuildCompra(new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc)),
            };

            var serie = VentasSerieBuilder.Build(compras, desde, hasta);

            Assert.Equal(12, serie.Count);
            var marzo = serie.Single(b => ArgentinaTimeZoneProvider.ToLocal(b.PeriodoDesde).Month == 3);
            Assert.Equal(2, marzo.CantidadCompras);
        }

        [Fact]
        public void Build_EtiquetaSemanal_MuestraRangoDeLunesADomingo()
        {
            var desde = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc); // lunes local
            var hasta = desde.AddDays(35); // > 31 días -> granularidad semanal

            var serie = VentasSerieBuilder.Build(new List<Compra>(), desde, hasta);

            Assert.Equal(VentasSerieGranularidad.Semanal, VentasSerieBuilder.DetermineGranularidad(hasta - desde));
            Assert.All(serie, bucket => Assert.Contains("–", bucket.Etiqueta));
        }

        [Fact]
        public void Build_SinCompras_TodosLosBucketsQuedanEnCero()
        {
            var desde = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
            var hasta = desde.AddDays(5);

            var serie = VentasSerieBuilder.Build(new List<Compra>(), desde, hasta);

            Assert.Equal(5, serie.Count);
            Assert.All(serie, b =>
            {
                Assert.Equal(0, b.CantidadCompras);
                Assert.Equal(0, b.EntradasEmitidas);
                Assert.Equal(0m, b.ImporteEmitido);
            });
        }

        [Fact]
        public void Build_PeriodosOrdenadosCronologicamente()
        {
            var desde = new DateTime(2026, 6, 1, 3, 0, 0, DateTimeKind.Utc);
            var hasta = desde.AddDays(10);

            var serie = VentasSerieBuilder.Build(new List<Compra>(), desde, hasta);

            var ordenado = serie.OrderBy(b => b.PeriodoDesde).ToList();
            Assert.Equal(ordenado.Select(b => b.PeriodoDesde), serie.Select(b => b.PeriodoDesde));
        }
    }
}
