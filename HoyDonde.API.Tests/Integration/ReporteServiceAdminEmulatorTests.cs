using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // GET /api/reports/admin/events (docs/api-mvp-plan.md §11.3) contra Firestore Emulator real:
    // sin organizadorPersonaId (cualquier organizador, solo rango), con organizadorPersonaId
    // (WhereEqualTo + rango, mismo índice compuesto que el reporte del Organizador),
    // estado/categoría en memoria, chunking de tickets. Mismo patrón de fixture que
    // ReporteServiceEmulatorTests.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class ReporteServiceAdminEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public ReporteServiceAdminEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static ReporteService CreateSut(FirestoreEmulatorFixture fixture)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            var eventService = new EventService(fixture.Db!, personaResolver.Object, new Mock<ILogger<EventService>>().Object);
            return new ReporteService(fixture.Db!, personaResolver.Object, eventService);
        }

        private static Event BuildEvent(
            string id,
            string organizadorPersonaId,
            Event.EventStatus estado = Event.EventStatus.Publicado,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null,
            List<TicketType>? ticketTypes = null,
            Event.EventCategory categoria = Event.EventCategory.Musica)
        {
            var tipos = ticketTypes ?? new List<TicketType>
            {
                new() { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 100, CantidadDisponible = 10 }
            };
            return new Event
            {
                Id = id,
                Nombre = "Evento admin de prueba",
                Descripcion = "Descripcion",
                Ubicacion = "Buenos Aires",
                Categoria = categoria,
                FechaInicio = fechaInicio ?? DateTime.UtcNow.AddDays(5),
                FechaFin = fechaFin ?? DateTime.UtcNow.AddDays(6),
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = estado,
                TicketTypes = tipos,
                CapacidadMaxima = tipos.Sum(t => t.CantidadDisponible),
            };
        }

        private async Task SeedEventAsync(Event evento) =>
            await _fixture.Db!.Collection("events").Document(evento.Id).SetAsync(evento);

        private async Task SeedTicketAsync(string eventId, string ticketTypeId, Ticket.TicketStatus estado, decimal precioPagado)
        {
            var ticket = new Ticket
            {
                Id = Guid.NewGuid().ToString(),
                EventoId = eventId,
                TicketTypeId = ticketTypeId,
                Estado = estado,
                ClientePersonaId = $"persona-cliente-{Guid.NewGuid():N}",
                PrecioPagado = precioPagado,
                FechaInicio = DateTime.UtcNow.AddDays(5),
                FechaFin = DateTime.UtcNow.AddDays(6),
            };
            await _fixture.Db!.Collection("tickets").Document(ticket.Id).SetAsync(ticket);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_SinOrganizador_IncluyeEventosDeCualquierOrganizador()
        {
            var baseFecha = DateTime.UtcNow.AddDays(600);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var organizadorB = $"persona-b-{Guid.NewGuid():N}";
            var eventoA = $"event-a-{Guid.NewGuid():N}";
            var eventoB = $"event-b-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventoA, organizadorA, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            await SeedEventAsync(BuildEvent(eventoB, organizadorB, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            var ids = resultado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(eventoA, ids);
            Assert.Contains(eventoB, ids);

            var detalleA = resultado.Eventos.Single(e => e.EventId == eventoA);
            var detalleB = resultado.Eventos.Single(e => e.EventId == eventoB);
            Assert.Equal(organizadorA, detalleA.OrganizadorPersonaId);
            Assert.Equal(organizadorB, detalleB.OrganizadorPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_ConOrganizadorPersonaId_SoloIncluyeEseOrganizador()
        {
            var baseFecha = DateTime.UtcNow.AddDays(610);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var organizadorB = $"persona-b-{Guid.NewGuid():N}";
            var eventoA = $"event-a-{Guid.NewGuid():N}";
            var eventoB = $"event-b-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventoA, organizadorA, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            await SeedEventAsync(BuildEvent(eventoB, organizadorB, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                OrganizadorPersonaId = organizadorA,
            });

            var ids = resultado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(eventoA, ids);
            Assert.DoesNotContain(eventoB, ids);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_FiltraPorEstadoYCategoriaEnMemoria()
        {
            var baseFecha = DateTime.UtcNow.AddDays(620);
            var sut = CreateSut(_fixture);

            var organizador = $"persona-{Guid.NewGuid():N}";
            var publicadoMusica = $"event-pub-mus-{Guid.NewGuid():N}";
            var borradorDeportes = $"event-bor-dep-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(publicadoMusica, organizador, estado: Event.EventStatus.Publicado, categoria: Event.EventCategory.Musica, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            await SeedEventAsync(BuildEvent(borradorDeportes, organizador, estado: Event.EventStatus.Borrador, categoria: Event.EventCategory.Deportes, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                Estado = Event.EventEffectiveStatus.Publicado,
                Categoria = Event.EventCategory.Musica,
            });

            var ids = resultado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(publicadoMusica, ids);
            Assert.DoesNotContain(borradorDeportes, ids);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_CalculaMetricasYAgregaImporteEmitido()
        {
            var baseFecha = DateTime.UtcNow.AddDays(630);
            var sut = CreateSut(_fixture);

            var organizador = $"persona-{Guid.NewGuid():N}";
            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 25, CantidadDisponible = 8 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, organizador, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2), ticketTypes: new List<TicketType> { tipo }));
            await SeedTicketAsync(eventId, tipo.Id, Ticket.TicketStatus.Usado, 25);
            await SeedTicketAsync(eventId, tipo.Id, Ticket.TicketStatus.Emitido, 25);

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(2, detalle.EntradasEmitidas);
            Assert.Equal(1, detalle.EntradasUsadas);
            Assert.Equal(1, detalle.EntradasPendientes);
            Assert.Equal(50m, resultado.Resumen.ImporteEmitido);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_CeroEventos_ReturnsEmptyReport()
        {
            var sut = CreateSut(_fixture);
            var baseFecha = DateTime.UtcNow.AddDays(640);

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha,
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Empty(resultado.Eventos);
            Assert.Equal(0, resultado.Resumen.CantidadEventos);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_RangoInvalido_ThrowsReporteRangoInvalidoException()
        {
            var sut = CreateSut(_fixture);
            var ahora = DateTime.UtcNow;

            await Assert.ThrowsAsync<Exceptions.ReporteRangoInvalidoException>(() => sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = ahora,
                FechaHasta = ahora.AddDays(400),
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminEventsReportAsync_MasDeTreintaEventosDeDistintosOrganizadores_ChunkingSinPerdidas()
        {
            var baseFecha = DateTime.UtcNow.AddDays(650);
            var sut = CreateSut(_fixture);

            const int cantidadEventos = 35;
            var idsEsperados = new List<string>();

            for (int i = 0; i < cantidadEventos; i++)
            {
                var tipo = new TicketType { Id = $"tipo-{i}-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
                var organizador = $"persona-{i}-{Guid.NewGuid():N}";
                var eventId = $"event-admin-chunk-{i}-{Guid.NewGuid():N}";
                idsEsperados.Add(eventId);
                await SeedEventAsync(BuildEvent(eventId, organizador, fechaInicio: baseFecha.AddHours(i), fechaFin: baseFecha.AddDays(10), ticketTypes: new List<TicketType> { tipo }));
                await SeedTicketAsync(eventId, tipo.Id, Ticket.TicketStatus.Emitido, 10);
            }

            var resultado = await sut.GetAdminEventsReportAsync(new ReporteAdminEventosFilterDto
            {
                FechaDesde = baseFecha.AddMinutes(-1),
                FechaHasta = baseFecha.AddDays(5),
            });

            var idsDevueltos = resultado.Eventos.Select(e => e.EventId).ToList();

            Assert.Equal(cantidadEventos, idsDevueltos.Count);
            Assert.Equal(idsDevueltos.Count, idsDevueltos.Distinct().Count());
            Assert.Equal(idsEsperados.OrderBy(x => x).ToList(), idsDevueltos.OrderBy(x => x).ToList());
            Assert.All(resultado.Eventos, e => Assert.Equal(1, e.EntradasEmitidas));
            Assert.Equal(cantidadEventos, resultado.Resumen.EntradasEmitidas);
        }
    }
}
