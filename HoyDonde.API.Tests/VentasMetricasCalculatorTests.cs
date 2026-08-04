using System;
using System.Collections.Generic;
using System.Linq;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Agregación pura del reporte de ventas simuladas (docs/api-mvp-plan.md §11): fórmulas de
    // resumen, redondeo, división por cero, clientes únicos, destacados, Top 5 determinístico,
    // desglose por categoría/tipo de entrada y sus porcentajes. Sin Firestore: todo se ejercita con
    // objetos Compra/Ticket en memoria (ver Integration/VentasReporteServiceEmulatorTests para el
    // recorrido completo contra Firestore, y VentasSerieBuilderTests para la serie temporal).
    public class VentasMetricasCalculatorTests
    {
        private static readonly DateTime Desde = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime Hasta = new(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);

        private static Compra BuildCompra(
            string eventoId,
            string eventoNombre,
            string clientePersonaId,
            int cantidad,
            decimal importe,
            Event.EventCategory? categoria = Event.EventCategory.Musica,
            DateTime? fechaCompra = null) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ClientePersonaId = clientePersonaId,
            EventoId = eventoId,
            EventoNombre = eventoNombre,
            FechaCompra = fechaCompra ?? Desde.AddDays(1),
            CantidadEntradas = cantidad,
            ImporteTotal = importe,
            Categoria = categoria,
        };

        [Fact]
        public void Build_SinCompras_ReturnsZeroedResumen_AndNullDestacados()
        {
            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, new List<Compra>(), new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(0, resultado.Resumen.CantidadCompras);
            Assert.Equal(0, resultado.Resumen.EntradasEmitidas);
            Assert.Equal(0m, resultado.Resumen.ImporteEmitido);
            Assert.Equal(0m, resultado.Resumen.ImportePromedioPorCompra);
            Assert.Equal(0m, resultado.Resumen.PrecioPromedioEntrada);
            Assert.Equal(0, resultado.Resumen.ClientesUnicos);
            Assert.Null(resultado.Resumen.EventoConMayorImporte);
            Assert.Null(resultado.Resumen.EventoConMasEntradas);
            Assert.Empty(resultado.TopEventos);
            Assert.Empty(resultado.PorCategoria);
            Assert.Empty(resultado.PorTipoEntrada);
        }

        [Fact]
        public void Build_Resumen_CalculatesPromediosCorrectly()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 2, 100m),
                BuildCompra("event-1", "Festival", "persona-b", 3, 150m),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(2, resultado.Resumen.CantidadCompras);
            Assert.Equal(5, resultado.Resumen.EntradasEmitidas);
            Assert.Equal(250m, resultado.Resumen.ImporteEmitido);
            Assert.Equal(125m, resultado.Resumen.ImportePromedioPorCompra); // 250/2
            Assert.Equal(50m, resultado.Resumen.PrecioPromedioEntrada); // 250/5
            Assert.Equal(2, resultado.Resumen.ClientesUnicos);
        }

        [Fact]
        public void Build_ClientesUnicos_CountsDistinctClientePersonaId_ButNeverExposesThem()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 1, 10m),
                BuildCompra("event-1", "Festival", "persona-a", 1, 10m), // mismo cliente, dos compras
                BuildCompra("event-1", "Festival", "persona-b", 1, 10m),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(2, resultado.Resumen.ClientesUnicos);
            Assert.Equal(3, resultado.Resumen.CantidadCompras);

            var prohibido = new[] { "ClientePersonaId", "OrganizadorPersonaId", "UsuarioId", "Uid" };
            foreach (var tipo in new[] { typeof(DTOs.VentasReporteResponseDto), typeof(DTOs.VentasResumenDto), typeof(DTOs.VentasTopEventoDto), typeof(DTOs.VentasCategoriaDto), typeof(DTOs.VentasTicketTypeDto) })
            {
                var propiedades = tipo.GetProperties().Select(p => p.Name).ToList();
                foreach (var nombreProhibido in prohibido)
                    Assert.DoesNotContain(nombreProhibido, propiedades);
            }
        }

        [Fact]
        public void Build_EventoConMayorImporte_And_EventoConMasEntradas_CanDiffer()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-caro", "Evento caro", "persona-a", 1, 1000m),
                BuildCompra("event-popular", "Evento popular", "persona-b", 20, 200m),
                BuildCompra("event-popular", "Evento popular", "persona-c", 20, 200m),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal("event-caro", resultado.Resumen.EventoConMayorImporte!.EventoId); // 1000 > 400
            Assert.Equal("event-popular", resultado.Resumen.EventoConMasEntradas!.EventoId); // 40 > 1
        }

        [Fact]
        public void Build_TopEventos_OrdersByImporteDesc_ThenEntradasDesc_ThenNombre_ThenId_MaxFive()
        {
            var compras = new List<Compra>();
            for (int i = 0; i < 7; i++)
            {
                compras.Add(BuildCompra($"event-{i}", $"Evento {i}", $"persona-{i}", 1, 100m - i)); // importe descendente por índice
            }
            // Empate de importe entre event-a y event-b: desempata por EntradasEmitidas.
            compras.Add(BuildCompra("event-empate-a", "Empate", "persona-x", 1, 500m));
            compras.Add(BuildCompra("event-empate-b", "Empate", "persona-y", 5, 500m));

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(5, resultado.TopEventos.Count);
            Assert.Equal("event-empate-b", resultado.TopEventos[0].EventoId); // 500 importe, 5 entradas gana a 1 entrada
            Assert.Equal("event-empate-a", resultado.TopEventos[1].EventoId);
            Assert.True(resultado.TopEventos.Zip(resultado.TopEventos.Skip(1), (a, b) => a.ImporteEmitido >= b.ImporteEmitido).All(x => x));
        }

        [Fact]
        public void Build_TopEventos_ImportePromedioCompra_IsImporteDividedByCantidadCompras()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 1, 100m),
                BuildCompra("event-1", "Festival", "persona-b", 1, 300m),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            var top = Assert.Single(resultado.TopEventos);
            Assert.Equal(2, top.CantidadCompras);
            Assert.Equal(400m, top.ImporteEmitido);
            Assert.Equal(200m, top.ImportePromedioCompra);
        }

        [Fact]
        public void Build_PorCategoria_GroupsByCategoria_AndComputesPercentages()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 1, 300m, Event.EventCategory.Musica),
                BuildCompra("event-2", "Maraton", "persona-b", 1, 100m, Event.EventCategory.Deportes),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(2, resultado.PorCategoria.Count);
            var musica = resultado.PorCategoria.Single(c => c.Categoria == "Musica");
            Assert.Equal(75.0, musica.PorcentajeDelImporteTotal); // 300/400 * 100
            var deportes = resultado.PorCategoria.Single(c => c.Categoria == "Deportes");
            Assert.Equal(25.0, deportes.PorcentajeDelImporteTotal);
        }

        [Fact]
        public void Build_PorCategoria_CompraLegacySinCategoria_AgrupaComoSinCategoria_NeverInventsAValue()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 1, 100m, categoria: null),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            var fila = Assert.Single(resultado.PorCategoria);
            Assert.Equal("Sin categoría", fila.Categoria);
        }

        [Fact]
        public void Build_PorTipoEntrada_EmptyWithoutEventIdFilter()
        {
            var compras = new List<Compra> { BuildCompra("event-1", "Festival", "persona-a", 1, 100m) };
            var tickets = new Dictionary<string, List<Ticket>>
            {
                [compras[0].Id] = new List<Ticket> { new() { Id = "t1", CompraId = compras[0].Id, TicketTypeId = "tipo-1", TicketTypeNombre = "General", PrecioPagado = 100m } }
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, tickets, eventIdFiltro: null);

            Assert.Empty(resultado.PorTipoEntrada);
        }

        [Fact]
        public void Build_PorTipoEntrada_WithEventIdFilter_GroupsByTicketType_AndComputesPercentages()
        {
            var compraA = BuildCompra("event-1", "Festival", "persona-a", 1, 100m);
            var compraB = BuildCompra("event-1", "Festival", "persona-b", 1, 300m);
            var compras = new List<Compra> { compraA, compraB };
            var tickets = new Dictionary<string, List<Ticket>>
            {
                [compraA.Id] = new List<Ticket> { new() { Id = "t1", CompraId = compraA.Id, TicketTypeId = "tipo-general", TicketTypeNombre = "General", PrecioPagado = 100m } },
                [compraB.Id] = new List<Ticket> { new() { Id = "t2", CompraId = compraB.Id, TicketTypeId = "tipo-vip", TicketTypeNombre = "VIP", PrecioPagado = 300m } },
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, tickets, eventIdFiltro: "event-1");

            Assert.Equal(2, resultado.PorTipoEntrada.Count);
            var vip = resultado.PorTipoEntrada.Single(t => t.TicketTypeId == "tipo-vip");
            Assert.Equal(1, vip.CantidadComprasDistintas);
            Assert.Equal(1, vip.EntradasEmitidas);
            Assert.Equal(300m, vip.ImporteEmitido);
            Assert.Equal(75.0, vip.PorcentajeDelImporteTotal);
        }

        [Fact]
        public void Build_PorTipoEntrada_TicketsLegacySinCompraId_NeverIncluded()
        {
            var compra = BuildCompra("event-1", "Festival", "persona-a", 1, 100m);
            var tickets = new Dictionary<string, List<Ticket>>
            {
                [compra.Id] = new List<Ticket> { new() { Id = "t1", CompraId = compra.Id, TicketTypeId = "tipo-1", TicketTypeNombre = "General", PrecioPagado = 100m } },
                // Un ticket legacy sin CompraId nunca llegaría acá en la práctica (ver
                // VentasReporteService.GetTicketsPorCompraAsync), pero se verifica igual que si
                // apareciera indexado bajo una clave ajena a `compras`, no se cuenta.
                ["compra-inexistente"] = new List<Ticket> { new() { Id = "t2", CompraId = null, TicketTypeId = "tipo-legacy", TicketTypeNombre = "Legacy", PrecioPagado = 999m } },
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, new List<Compra> { compra }, tickets, eventIdFiltro: "event-1");

            Assert.Single(resultado.PorTipoEntrada);
            Assert.DoesNotContain(resultado.PorTipoEntrada, t => t.TicketTypeId == "tipo-legacy");
        }

        [Fact]
        public void Build_ComprasSinTickets_HandledSafely_NoException()
        {
            var compra = BuildCompra("event-1", "Festival", "persona-a", 1, 100m);
            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, new List<Compra> { compra }, new Dictionary<string, List<Ticket>>(), eventIdFiltro: "event-1");

            Assert.Empty(resultado.PorTipoEntrada);
            Assert.Equal(1, resultado.Resumen.CantidadCompras);
        }

        [Fact]
        public void Build_ImporteEmitido_RoundedToTwoDecimals()
        {
            var compras = new List<Compra>
            {
                BuildCompra("event-1", "Festival", "persona-a", 1, 33.333m),
                BuildCompra("event-1", "Festival", "persona-b", 1, 33.334m),
            };

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(66.67m, resultado.Resumen.ImporteEmitido);
        }

        [Fact]
        public void Build_MasDe30ComprasMismoEvento_AggregatesAllOfThem()
        {
            var compras = Enumerable.Range(0, 35)
                .Select(i => BuildCompra("event-1", "Festival", $"persona-{i}", 1, 10m))
                .ToList();

            var resultado = VentasMetricasCalculator.Build(Desde, Hasta, compras, new Dictionary<string, List<Ticket>>(), null);

            Assert.Equal(35, resultado.Resumen.CantidadCompras);
            Assert.Equal(350m, resultado.Resumen.ImporteEmitido);
        }
    }
}
