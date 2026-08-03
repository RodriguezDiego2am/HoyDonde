using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // GET /api/reports/organizer/events (docs/api-mvp-plan.md §11) contra Firestore Emulator
    // real: ownership siempre dentro de la propia query, límites Desde/Hasta, estado/categoría en
    // memoria, eventId propio/ajeno/inexistente, ticketType válido/inválido, chunking de
    // WhereIn(EventoId) para más de 30 eventos, y el nuevo índice compuesto
    // (OrganizadorPersonaId ASC, FechaInicio ASC) cargado por firestore.indexes.json.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class ReporteServiceEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public ReporteServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static ReporteService CreateSut(FirestoreEmulatorFixture fixture, params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }
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
                Nombre = "Evento de prueba",
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

        // ---- Ownership: la query siempre incluye OrganizadorPersonaId; eventos ajenos nunca participan ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_SoloIncluyeEventosPropios_NuncaAjenos()
        {
            var baseFecha = DateTime.UtcNow.AddDays(500);
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var otroPersonaId = $"persona-otro-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var propio = $"event-propio-{Guid.NewGuid():N}";
            var ajeno = $"event-ajeno-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(propio, duenoPersonaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            await SeedEventAsync(BuildEvent(ajeno, otroPersonaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));

            var resultado = await sut.GetOrganizerEventsReportAsync(duenoUid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            var ids = resultado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(propio, ids);
            Assert.DoesNotContain(ajeno, ids);
        }

        // ---- Límites de rango: Desde inclusivo, Hasta exclusivo ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_DesdeInclusive_HastaExclusive()
        {
            var baseFecha = DateTime.UtcNow.AddDays(510);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var desde = baseFecha;
            var hasta = baseFecha.AddDays(3);

            // Offsets estrictamente > 0 respecto de Desde/Hasta (nunca fechas exactamente iguales
            // al límite): el Firestore Emulator local tiene un comportamiento no confiable en el
            // límite exacto de una consulta con desigualdades sobre el mismo campo (mismo criterio
            // ya documentado en EventServiceEmulatorTests), así que Desde-inclusiva/Hasta-exclusiva
            // se verifican cerca del límite, no exactamente sobre él.
            var antesDeDesde = $"event-antes-{Guid.NewGuid():N}";
            var justoDespuesDeDesde = $"event-en-desde-{Guid.NewGuid():N}";
            var justoAntesDeHasta = $"event-antes-hasta-{Guid.NewGuid():N}";
            var justoDespuesDeHasta = $"event-en-hasta-{Guid.NewGuid():N}";

            await SeedEventAsync(BuildEvent(antesDeDesde, personaId, fechaInicio: desde.AddSeconds(-2), fechaFin: hasta.AddDays(5)));
            await SeedEventAsync(BuildEvent(justoDespuesDeDesde, personaId, fechaInicio: desde.AddSeconds(1), fechaFin: hasta.AddDays(5)));
            await SeedEventAsync(BuildEvent(justoAntesDeHasta, personaId, fechaInicio: hasta.AddSeconds(-1), fechaFin: hasta.AddDays(5)));
            await SeedEventAsync(BuildEvent(justoDespuesDeHasta, personaId, fechaInicio: hasta.AddSeconds(1), fechaFin: hasta.AddDays(5)));

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto { FechaDesde = desde, FechaHasta = hasta });
            var ids = resultado.Eventos.Select(e => e.EventId).ToList();

            Assert.DoesNotContain(antesDeDesde, ids);
            Assert.Contains(justoDespuesDeDesde, ids);
            Assert.Contains(justoAntesDeHasta, ids);
            Assert.DoesNotContain(justoDespuesDeHasta, ids);
        }

        // ---- Filtro de estado (efectivo) y categoría, en memoria ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_FiltraPorEstadoEfectivo_DistinguiendoFinalizadoDePublicado()
        {
            var baseFecha = DateTime.UtcNow.AddDays(520);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var borrador = $"event-borrador-{Guid.NewGuid():N}";
            var publicadoVigente = $"event-publicado-{Guid.NewGuid():N}";
            var finalizado = $"event-finalizado-{Guid.NewGuid():N}";

            await SeedEventAsync(BuildEvent(borrador, personaId, estado: Event.EventStatus.Borrador, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            await SeedEventAsync(BuildEvent(publicadoVigente, personaId, estado: Event.EventStatus.Publicado, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));
            // "Finalizado" real: FechaFin ya pasada, fuera del rango consultado en FechaInicio pero
            // el propio FechaInicio se fija dentro del rango para poder incluirlo en la query.
            await SeedEventAsync(BuildEvent(finalizado, personaId, estado: Event.EventStatus.Publicado, fechaInicio: baseFecha, fechaFin: DateTime.UtcNow.AddDays(-1)));

            var desde = baseFecha.AddDays(-1);
            var hasta = baseFecha.AddDays(3);

            var soloPublicado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto { FechaDesde = desde, FechaHasta = hasta, Estado = Event.EventEffectiveStatus.Publicado });
            var idsPublicado = soloPublicado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(publicadoVigente, idsPublicado);
            Assert.DoesNotContain(borrador, idsPublicado);
            Assert.DoesNotContain(finalizado, idsPublicado);

            var soloFinalizado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto { FechaDesde = desde, FechaHasta = hasta, Estado = Event.EventEffectiveStatus.Finalizado });
            var idsFinalizado = soloFinalizado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(finalizado, idsFinalizado);
            Assert.DoesNotContain(publicadoVigente, idsFinalizado);
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_FiltraPorCategoria()
        {
            var baseFecha = DateTime.UtcNow.AddDays(530);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var musica = $"event-musica-{Guid.NewGuid():N}";
            var deportes = $"event-deportes-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(musica, personaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2), categoria: Event.EventCategory.Musica));
            await SeedEventAsync(BuildEvent(deportes, personaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2), categoria: Event.EventCategory.Deportes));

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(3),
                Categoria = Event.EventCategory.Musica,
            });

            var ids = resultado.Eventos.Select(e => e.EventId).ToList();
            Assert.Contains(musica, ids);
            Assert.DoesNotContain(deportes, ids);
        }

        // ---- eventId: propio / ajeno / inexistente ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_EventIdPropio_ReturnsOnlyThatEvent_WithMetrics()
        {
            var baseFecha = DateTime.UtcNow.AddDays(540);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 50, CantidadDisponible = 9 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, personaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2), ticketTypes: new List<TicketType> { tipo }));
            await SeedTicketAsync(eventId, tipo.Id, Ticket.TicketStatus.Usado, 50);

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-100),
                FechaHasta = baseFecha.AddDays(100),
                EventId = eventId,
            });

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(eventId, detalle.EventId);
            Assert.Equal(1, detalle.EntradasEmitidas);
            Assert.Equal(1, detalle.EntradasUsadas);
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_EventIdAjeno_ThrowsEventOwnershipException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var otroPersonaId = $"persona-otro-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventId = $"event-ajeno-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, otroPersonaId));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                EventId = eventId,
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_EventIdInexistente_ThrowsEventNotFoundException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<EventNotFoundException>(() => sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                EventId = $"event-inexistente-{Guid.NewGuid():N}",
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_EventIdPropio_FueraDeRango_ReturnsEmptyReport_NoFuga()
        {
            var baseFecha = DateTime.UtcNow.AddDays(550);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, personaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2)));

            // Rango consultado deliberadamente fuera de la fecha real del evento.
            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(100),
                FechaHasta = baseFecha.AddDays(200),
                EventId = eventId,
            });

            Assert.Empty(resultado.Eventos);
            Assert.Equal(0, resultado.Resumen.CantidadEventos);
        }

        // ---- ticketType: válido / inválido ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_TicketTypeValido_AcotaMetricasAEseTipo()
        {
            var baseFecha = DateTime.UtcNow.AddDays(560);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipoA = new TicketType { Id = $"tipo-a-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
            var tipoB = new TicketType { Id = $"tipo-b-{Guid.NewGuid():N}", Nombre = "VIP", Precio = 50, CantidadDisponible = 2 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, personaId, fechaInicio: baseFecha, fechaFin: baseFecha.AddDays(2), ticketTypes: new List<TicketType> { tipoA, tipoB }));
            await SeedTicketAsync(eventId, tipoA.Id, Ticket.TicketStatus.Emitido, 10);
            await SeedTicketAsync(eventId, tipoB.Id, Ticket.TicketStatus.Usado, 50);

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(3),
                EventId = eventId,
                TicketTypeId = tipoA.Id,
            });

            var detalle = Assert.Single(resultado.Eventos);
            Assert.Equal(1, detalle.EntradasEmitidas);
            Assert.Equal(0, detalle.EntradasUsadas); // el ticket "Usado" es de tipoB, excluido
            var fila = Assert.Single(detalle.TiposDeEntrada);
            Assert.Equal(tipoA.Id, fila.TicketTypeId);
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_TicketTypeInvalido_ThrowsTicketTypeInvalidoException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(BuildEvent(eventId, personaId, ticketTypes: new List<TicketType> { tipo }));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<TicketTypeInvalidoException>(() => sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                EventId = eventId,
                TicketTypeId = "tipo-que-no-pertenece",
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_TicketTypeSinEventId_ThrowsReporteFiltroInvalidoException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<ReporteFiltroInvalidoException>(() => sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                TicketTypeId = "tipo-1",
            }));
        }

        // ---- Cero eventos ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_CeroEventos_ReturnsEmptyReport()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var baseFecha = DateTime.UtcNow.AddDays(570);

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha,
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Empty(resultado.Eventos);
            Assert.Equal(0, resultado.Resumen.CantidadEventos);
        }

        // ---- Más de 30 eventos: comprueba el chunking de WhereIn(EventoId) y ausencia de duplicados ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerEventsReportAsync_MasDeTreintaEventos_ChunkingDeTicketsSinDuplicadosNiPerdidas()
        {
            var baseFecha = DateTime.UtcNow.AddDays(580);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            const int cantidadEventos = 35;
            var idsEsperados = new List<string>();

            for (int i = 0; i < cantidadEventos; i++)
            {
                var tipo = new TicketType { Id = $"tipo-{i}-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
                var eventId = $"event-chunk-{i}-{Guid.NewGuid():N}";
                idsEsperados.Add(eventId);
                await SeedEventAsync(BuildEvent(eventId, personaId, fechaInicio: baseFecha.AddHours(i), fechaFin: baseFecha.AddDays(10), ticketTypes: new List<TicketType> { tipo }));
                await SeedTicketAsync(eventId, tipo.Id, Ticket.TicketStatus.Emitido, 10);
            }

            var resultado = await sut.GetOrganizerEventsReportAsync(uid, new ReporteEventosFilterDto
            {
                FechaDesde = baseFecha.AddMinutes(-1),
                FechaHasta = baseFecha.AddDays(5),
            });

            var idsDevueltos = resultado.Eventos.Select(e => e.EventId).ToList();

            Assert.Equal(cantidadEventos, idsDevueltos.Count);
            Assert.Equal(idsDevueltos.Count, idsDevueltos.Distinct().Count()); // sin duplicados
            Assert.Equal(idsEsperados.OrderBy(x => x).ToList(), idsDevueltos.OrderBy(x => x).ToList());

            // Cada evento tiene exactamente su propio ticket contado (chunking correcto, sin
            // mezclar tickets entre eventos de distintos lotes de <=30).
            Assert.All(resultado.Eventos, e => Assert.Equal(1, e.EntradasEmitidas));
            Assert.Equal(cantidadEventos, resultado.Resumen.EntradasEmitidas);
        }
    }
}
