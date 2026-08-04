using System;
using System.Collections.Generic;
using System.Linq;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Agregación pura del reporte de eventos (docs/api-mvp-plan.md §11.9): fórmulas, redondeo,
    // división por cero, capacidad global vs. derivada por tipo, y el colapso del desglose cuando
    // el filtro trae ticketTypeId. Sin Firestore: todo se ejercita con objetos Event/Ticket en
    // memoria (ver ReporteFiltroValidatorTests para la validación de rango/ownership/filtros, y
    // Integration/ReporteServiceEmulatorTests para el recorrido completo contra Firestore).
    public class ReporteMetricasCalculatorTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        private static TicketType BuildTicketType(string id, string nombre, decimal precio, int cantidadDisponible) => new()
        {
            Id = id,
            Nombre = nombre,
            Precio = precio,
            CantidadDisponible = cantidadDisponible,
        };

        private static Event BuildEvento(string id, int capacidadMaxima, List<TicketType> ticketTypes, Event.EventStatus estado = Event.EventStatus.Publicado, DateTime? fechaFin = null) => new()
        {
            Id = id,
            Nombre = "Evento de prueba",
            Ubicacion = "Buenos Aires",
            Categoria = Event.EventCategory.Musica,
            FechaInicio = UtcNow.AddDays(-1),
            FechaFin = fechaFin ?? UtcNow.AddDays(10),
            Estado = estado,
            CapacidadMaxima = capacidadMaxima,
            TicketTypes = ticketTypes,
            OrganizadorPersonaId = "persona-organizador",
        };

        private static Ticket BuildTicket(string ticketTypeId, Ticket.TicketStatus estado, decimal precioPagado) => new()
        {
            Id = Guid.NewGuid().ToString(),
            TicketTypeId = ticketTypeId,
            Estado = estado,
            PrecioPagado = precioPagado,
        };

        [Fact]
        public void Build_EventoSinTickets_ReturnsZeroedMetrics_ButCapacidadFromCapacidadMaxima()
        {
            var tipo = BuildTicketType("tipo-1", "General", 100, 50);
            var evento = BuildEvento("event-1", capacidadMaxima: 50, ticketTypes: new List<TicketType> { tipo });
            var tickets = new Dictionary<string, List<Ticket>>();

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(50, detalle.CapacidadInicial);
            Assert.Equal(50, detalle.StockDisponible);
            Assert.Equal(0, detalle.EntradasEmitidas);
            Assert.Equal(0, detalle.EntradasUsadas);
            Assert.Equal(0, detalle.EntradasAnuladas);
            Assert.Equal(0, detalle.EntradasPendientes);
            Assert.Equal(0, detalle.PorcentajeOcupacion);
            Assert.Equal(0, detalle.PorcentajeAsistencia);
            Assert.Equal(0, detalle.PorcentajeUtilizacion);
            Assert.Equal(0, detalle.ImporteEmitido);
        }

        [Fact]
        public void Build_CountsEmitidasUsadasAnuladasPendientes_Correctly()
        {
            var tipo = BuildTicketType("tipo-1", "General", 100, 6);
            var evento = BuildEvento("event-1", capacidadMaxima: 10, ticketTypes: new List<TicketType> { tipo });
            var ticketsDelEvento = new List<Ticket>
            {
                BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 100),
                BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 100),
                BuildTicket("tipo-1", Ticket.TicketStatus.Anulado, 100),
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 100),
            };
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = ticketsDelEvento };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(4, detalle.EntradasEmitidas);
            Assert.Equal(2, detalle.EntradasUsadas);
            Assert.Equal(1, detalle.EntradasAnuladas);
            Assert.Equal(1, detalle.EntradasPendientes);
        }

        [Fact]
        public void Build_ImporteEmitido_SumsPrecioPagado_RoundedToTwoDecimals()
        {
            var tipo = BuildTicketType("tipo-1", "General", 33.333m, 10);
            var evento = BuildEvento("event-1", capacidadMaxima: 10, ticketTypes: new List<TicketType> { tipo });
            var ticketsDelEvento = new List<Ticket>
            {
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 33.333m),
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 33.333m),
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 33.334m),
            };
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = ticketsDelEvento };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(100.00m, detalle.ImporteEmitido);
            Assert.Equal(100.00m, resultado.Resumen.ImporteEmitido);
        }

        [Fact]
        public void Build_Porcentajes_DivisionPorCero_ReturnsZero_NuncaExcepcion()
        {
            // CapacidadMaxima 0 (evento sin tipos de ticket): Ocupación/Utilización deben dar 0,
            // no lanzar DivideByZeroException.
            var evento = BuildEvento("event-1", capacidadMaxima: 0, ticketTypes: new List<TicketType>());
            var tickets = new Dictionary<string, List<Ticket>>();

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(0, detalle.PorcentajeOcupacion);
            Assert.Equal(0, detalle.PorcentajeAsistencia);
            Assert.Equal(0, detalle.PorcentajeUtilizacion);
        }

        [Fact]
        public void Build_Porcentajes_RoundedExplicitlyToTwoDecimals_AsPercentage()
        {
            var tipo = BuildTicketType("tipo-1", "General", 10, 1); // stock restante 1, capacidad 3 (1 stock + 2 emitidas)
            var evento = BuildEvento("event-1", capacidadMaxima: 3, ticketTypes: new List<TicketType> { tipo });
            var ticketsDelEvento = new List<Ticket>
            {
                BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 10),
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 10),
            };
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = ticketsDelEvento };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            // Ocupacion = Emitidas(2)/CapacidadMaxima(3) = 66.666...% -> 66.67
            Assert.Equal(66.67, detalle.PorcentajeOcupacion);
            // Asistencia = Usadas(1)/Emitidas(2) = 50%
            Assert.Equal(50.0, detalle.PorcentajeAsistencia);
            // Utilizacion = Usadas(1)/CapacidadMaxima(3) = 33.333...% -> 33.33
            Assert.Equal(33.33, detalle.PorcentajeUtilizacion);
        }

        [Fact]
        public void Build_SinFiltroTicketType_CapacidadInicial_UsaCapacidadMaximaDelEvento()
        {
            var tipoA = BuildTicketType("tipo-a", "General", 10, 5);
            var tipoB = BuildTicketType("tipo-b", "VIP", 50, 2);
            var evento = BuildEvento("event-1", capacidadMaxima: 7, ticketTypes: new List<TicketType> { tipoA, tipoB });

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, new Dictionary<string, List<Ticket>>(), null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(7, detalle.CapacidadInicial);
            Assert.Equal(2, detalle.TiposDeEntrada.Count);
        }

        [Fact]
        public void Build_ConFiltroTicketType_CapacidadInicial_EsDerivada_StockMasEmitidas_YColapsaDesglose()
        {
            var tipoA = BuildTicketType("tipo-a", "General", 10, 3); // stock 3
            var tipoB = BuildTicketType("tipo-b", "VIP", 50, 8);
            var evento = BuildEvento("event-1", capacidadMaxima: 11, ticketTypes: new List<TicketType> { tipoA, tipoB });
            var ticketsDelEvento = new List<Ticket>
            {
                BuildTicket("tipo-a", Ticket.TicketStatus.Usado, 10),
                BuildTicket("tipo-a", Ticket.TicketStatus.Emitido, 10),
                BuildTicket("tipo-b", Ticket.TicketStatus.Usado, 50), // no debe contarse: filtrado por tipo-a
            };
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = ticketsDelEvento };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, "tipo-a", UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            // Capacidad derivada de tipo-a: stock actual (3) + emitidas de ese tipo (2) = 5.
            Assert.Equal(5, detalle.CapacidadInicial);
            Assert.Equal(3, detalle.StockDisponible);
            Assert.Equal(2, detalle.EntradasEmitidas);
            Assert.Equal(1, detalle.EntradasUsadas);
            var fila = Assert.Single(detalle.TiposDeEntrada);
            Assert.Equal("tipo-a", fila.TicketTypeId);
        }

        [Fact]
        public void Build_MultiplesEventos_Resumen_AgregaSobreTodos()
        {
            var tipo1 = BuildTicketType("tipo-1", "General", 10, 5);
            var evento1 = BuildEvento("event-1", capacidadMaxima: 5, ticketTypes: new List<TicketType> { tipo1 });
            var tipo2 = BuildTicketType("tipo-2", "General", 20, 8);
            var evento2 = BuildEvento("event-2", capacidadMaxima: 8, ticketTypes: new List<TicketType> { tipo2 });

            var tickets = new Dictionary<string, List<Ticket>>
            {
                ["event-1"] = new List<Ticket> { BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 10) },
                ["event-2"] = new List<Ticket> { BuildTicket("tipo-2", Ticket.TicketStatus.Emitido, 20), BuildTicket("tipo-2", Ticket.TicketStatus.Anulado, 20) },
            };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento1, evento2 }, tickets, null, UtcNow);

            Assert.Equal(2, resultado.Resumen.CantidadEventos);
            Assert.Equal(13, resultado.Resumen.CapacidadInicial);
            Assert.Equal(3, resultado.Resumen.EntradasEmitidas);
            Assert.Equal(1, resultado.Resumen.EntradasUsadas);
            Assert.Equal(1, resultado.Resumen.EntradasAnuladas);
            Assert.Equal(1, resultado.Resumen.EntradasPendientes);
            Assert.Equal(50.0m, resultado.Resumen.ImporteEmitido);
        }

        [Fact]
        public void Build_ZeroEventos_ReturnsEmptyReport_WithZeroedResumen()
        {
            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event>(), new Dictionary<string, List<Ticket>>(), null, UtcNow);

            Assert.Empty(resultado.Eventos);
            Assert.Equal(0, resultado.Resumen.CantidadEventos);
            Assert.Equal(0, resultado.Resumen.EntradasEmitidas);
        }

        [Fact]
        public void Build_EventoCanceladoOFinalizado_ConservaEstadoHistoricoDeTickets_NuncaUtilizableMotivo()
        {
            var tipo = BuildTicketType("tipo-1", "General", 10, 0);
            var evento = BuildEvento("event-1", capacidadMaxima: 2, ticketTypes: new List<TicketType> { tipo }, estado: Event.EventStatus.Cancelado);
            var ticketsDelEvento = new List<Ticket>
            {
                BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 10),
                BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 10),
            };
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = ticketsDelEvento };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal("Cancelado", detalle.Estado);
            // El ticket "Usado" sigue contando como Usado (estado histórico), pese a la
            // cancelación del evento: nunca se usa Utilizable/MotivoNoUtilizable acá.
            Assert.Equal(1, detalle.EntradasUsadas);
            Assert.Equal(1, detalle.EntradasPendientes);
        }

        [Fact]
        public void Build_EventoFinalizado_EstadoEfectivo_EsDistintoDePublicado()
        {
            var evento = BuildEvento("event-1", capacidadMaxima: 1, ticketTypes: new List<TicketType>(), estado: Event.EventStatus.Publicado, fechaFin: UtcNow.AddDays(-1));

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, new Dictionary<string, List<Ticket>>(), null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal("Finalizado", detalle.Estado);
        }

        // ---- Entradas no utilizadas: solo para eventos efectivamente Finalizados ----

        [Fact]
        public void Build_EventoFinalizado_CalculatesEntradasNoUtilizadas()
        {
            var tipo = BuildTicketType("tipo-1", "General", 10, 0);
            var evento = BuildEvento("event-1", capacidadMaxima: 3, ticketTypes: new List<TicketType> { tipo }, fechaFin: UtcNow.AddDays(-1));
            var tickets = new Dictionary<string, List<Ticket>>
            {
                ["event-1"] = new List<Ticket>
                {
                    BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 10),
                    BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 10),
                    BuildTicket("tipo-1", Ticket.TicketStatus.Anulado, 10),
                }
            };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal("Finalizado", detalle.Estado);
            // Emitidas(3) - Usadas(1) = 2 no utilizadas (pendiente + anulado).
            Assert.Equal(2, detalle.EntradasNoUtilizadas);
            Assert.Equal(66.67, detalle.PorcentajeNoUtilizacion);
            Assert.Equal(2, resultado.Resumen.EntradasNoUtilizadasFinalizados);
            Assert.Equal(66.67, resultado.Resumen.PorcentajeNoUtilizacionFinalizados);
        }

        [Theory]
        [InlineData(Event.EventStatus.Borrador)]
        [InlineData(Event.EventStatus.Publicado)]
        [InlineData(Event.EventStatus.Cancelado)]
        public void Build_EventoNoFinalizado_EntradasNoUtilizadas_IsNull_NeverCalledAusentismo(Event.EventStatus estado)
        {
            var tipo = BuildTicketType("tipo-1", "General", 10, 0);
            var evento = BuildEvento("event-1", capacidadMaxima: 1, ticketTypes: new List<TicketType> { tipo }, estado: estado, fechaFin: UtcNow.AddDays(5));
            var tickets = new Dictionary<string, List<Ticket>> { ["event-1"] = new List<Ticket> { BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 10) } };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(10), new List<Event> { evento }, tickets, null, UtcNow);

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Null(detalle.EntradasNoUtilizadas);
            Assert.Null(detalle.PorcentajeNoUtilizacion);
        }

        [Fact]
        public void Build_ResumenNoUtilizacion_OnlyAggregatesFinalizadoEvents()
        {
            var tipoFinalizado = BuildTicketType("tipo-1", "General", 10, 0);
            var eventoFinalizado = BuildEvento("event-fin", capacidadMaxima: 2, ticketTypes: new List<TicketType> { tipoFinalizado }, fechaFin: UtcNow.AddDays(-1));
            var tipoVigente = BuildTicketType("tipo-2", "General", 10, 0);
            var eventoVigente = BuildEvento("event-vig", capacidadMaxima: 2, ticketTypes: new List<TicketType> { tipoVigente }, fechaFin: UtcNow.AddDays(5));

            var tickets = new Dictionary<string, List<Ticket>>
            {
                ["event-fin"] = new List<Ticket> { BuildTicket("tipo-1", Ticket.TicketStatus.Emitido, 10), BuildTicket("tipo-1", Ticket.TicketStatus.Usado, 10) },
                ["event-vig"] = new List<Ticket> { BuildTicket("tipo-2", Ticket.TicketStatus.Emitido, 10) },
            };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(10), new List<Event> { eventoFinalizado, eventoVigente }, tickets, null, UtcNow);

            // Solo event-fin aporta al agregado: Emitidas(2) - Usadas(1) = 1.
            Assert.Equal(1, resultado.Resumen.EntradasNoUtilizadasFinalizados);
            Assert.Equal(50.0, resultado.Resumen.PorcentajeNoUtilizacionFinalizados);
        }

        [Fact]
        public void Build_SinEventosFinalizados_ResumenNoUtilizacion_IsZero_NeverException()
        {
            var evento = BuildEvento("event-1", capacidadMaxima: 1, ticketTypes: new List<TicketType>(), fechaFin: UtcNow.AddDays(5));
            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(10), new List<Event> { evento }, new Dictionary<string, List<Ticket>>(), null, UtcNow);

            Assert.Equal(0, resultado.Resumen.EntradasNoUtilizadasFinalizados);
            Assert.Equal(0, resultado.Resumen.PorcentajeNoUtilizacionFinalizados);
        }

        // ---- Destacados y Top 5 por importe emitido ----

        [Fact]
        public void Build_Destacados_IdentifiesMayorOcupacionAsistenciaImporte()
        {
            var tipoBajo = BuildTicketType("tipo-bajo", "General", 10, 8); // capacidad 10, ocupación baja
            var eventoBajo = BuildEvento("event-bajo", capacidadMaxima: 10, ticketTypes: new List<TicketType> { tipoBajo });
            var tipoAlto = BuildTicketType("tipo-alto", "General", 50, 0); // capacidad 2, ocupación/asistencia/importe altos
            var eventoAlto = BuildEvento("event-alto", capacidadMaxima: 2, ticketTypes: new List<TicketType> { tipoAlto });

            var tickets = new Dictionary<string, List<Ticket>>
            {
                ["event-bajo"] = new List<Ticket> { BuildTicket("tipo-bajo", Ticket.TicketStatus.Emitido, 10) },
                ["event-alto"] = new List<Ticket>
                {
                    BuildTicket("tipo-alto", Ticket.TicketStatus.Usado, 50),
                    BuildTicket("tipo-alto", Ticket.TicketStatus.Usado, 50),
                },
            };

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event> { eventoBajo, eventoAlto }, tickets, null, UtcNow);

            Assert.Equal("event-alto", resultado.Destacados.EventoMayorOcupacion!.EventId);
            Assert.Equal("event-alto", resultado.Destacados.EventoMayorAsistencia!.EventId);
            Assert.Equal("event-alto", resultado.Destacados.EventoMayorImporte!.EventId);
            Assert.Equal(100m, resultado.Destacados.EventoMayorImporte.ImporteEmitido);
        }

        [Fact]
        public void Build_Top5PorImporte_OrdersDescending_MaxFiveEntries()
        {
            var eventos = new List<Event>();
            var tickets = new Dictionary<string, List<Ticket>>();
            for (int i = 0; i < 7; i++)
            {
                var tipo = BuildTicketType($"tipo-{i}", "General", 10 + i, 0);
                var evento = BuildEvento($"event-{i}", capacidadMaxima: 1, ticketTypes: new List<TicketType> { tipo });
                eventos.Add(evento);
                tickets[$"event-{i}"] = new List<Ticket> { BuildTicket($"tipo-{i}", Ticket.TicketStatus.Usado, 10 + i) };
            }

            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), eventos, tickets, null, UtcNow);

            Assert.Equal(5, resultado.Destacados.Top5PorImporte.Count);
            Assert.Equal("event-6", resultado.Destacados.Top5PorImporte[0].EventId); // importe 16, el mayor
            Assert.True(resultado.Destacados.Top5PorImporte.Zip(resultado.Destacados.Top5PorImporte.Skip(1), (a, b) => a.ImporteEmitido >= b.ImporteEmitido).All(x => x));
        }

        [Fact]
        public void Build_ZeroEventos_Destacados_AreAllNullOrEmpty()
        {
            var resultado = ReporteMetricasCalculator.Build(UtcNow.AddDays(-5), UtcNow.AddDays(5), new List<Event>(), new Dictionary<string, List<Ticket>>(), null, UtcNow);

            Assert.Null(resultado.Destacados.EventoMayorOcupacion);
            Assert.Null(resultado.Destacados.EventoMayorAsistencia);
            Assert.Null(resultado.Destacados.EventoMayorImporte);
            Assert.Empty(resultado.Destacados.Top5PorImporte);
        }

        [Fact]
        public void ResponseDtos_NeverExposeInternalIdentifiers()
        {
            // DTO sin ids internos (docs/api-mvp-plan.md §11.9): ningún tipo del reporte expone
            // OrganizadorPersonaId, UID/ExternalSubjectId ni UsuarioId.
            var prohibido = new[] { "OrganizadorPersonaId", "UsuarioId", "ExternalSubjectId", "Uid", "PersonaId" };

            foreach (var tipo in new[] { typeof(ReporteEventosResponseDto), typeof(ReporteResumenDto), typeof(ReporteEventoDetalleDto), typeof(ReporteTicketTypeDetalleDto) })
            {
                var propiedades = tipo.GetProperties().Select(p => p.Name).ToList();
                foreach (var nombreProhibido in prohibido)
                {
                    Assert.DoesNotContain(nombreProhibido, propiedades);
                }
            }
        }
    }
}
