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
    // GET /api/reports/organizer/sales y GET /api/reports/admin/sales (docs/api-mvp-plan.md §11)
    // contra Firestore Emulator real: ownership siempre dentro de la propia query sobre
    // Compra.FechaCompra (nunca Event.FechaInicio), rango Desde inclusivo/Hasta exclusivo, Admin
    // sin/con organizador (mismo índice compuesto compras: OrganizadorPersonaId ASC, FechaCompra
    // ASC de firestore.indexes.json), eventId propio/ajeno/inexistente, categoría en memoria,
    // chunking de tickets por CompraId y exclusión de tickets legacy sin CompraId.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class VentasReporteServiceEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public VentasReporteServiceEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static VentasReporteService CreateSut(FirestoreEmulatorFixture fixture, params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }
            var eventService = new EventService(fixture.Db!, personaResolver.Object, new Mock<ILogger<EventService>>().Object);
            return new VentasReporteService(fixture.Db!, personaResolver.Object, eventService);
        }

        private async Task SeedEventAsync(
            string id,
            string organizadorPersonaId,
            Event.EventCategory categoria = Event.EventCategory.Musica,
            List<TicketType>? ticketTypes = null)
        {
            var tipos = ticketTypes ?? new List<TicketType> { new() { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 100 } };
            await _fixture.Db!.Collection("events").Document(id).SetAsync(new Event
            {
                Id = id,
                Nombre = "Evento de ventas",
                Ubicacion = "Buenos Aires",
                Categoria = categoria,
                FechaInicio = DateTime.UtcNow.AddDays(30),
                FechaFin = DateTime.UtcNow.AddDays(31),
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = Event.EventStatus.Publicado,
                TicketTypes = tipos,
                CapacidadMaxima = tipos.Sum(t => t.CantidadDisponible),
            });
        }

        private async Task<string> SeedCompraAsync(
            string eventoId,
            string organizadorPersonaId,
            string clientePersonaId,
            DateTime fechaCompra,
            int cantidad = 1,
            decimal importe = 10m,
            Event.EventCategory? categoria = Event.EventCategory.Musica,
            string eventoNombre = "Evento de ventas")
        {
            var compraId = Guid.NewGuid().ToString();
            await _fixture.Db!.Collection("compras").Document(compraId).SetAsync(new Compra
            {
                Id = compraId,
                ClientePersonaId = clientePersonaId,
                EventoId = eventoId,
                EventoNombre = eventoNombre,
                Ubicacion = "Buenos Aires",
                FechaCompra = fechaCompra,
                CantidadEntradas = cantidad,
                ImporteTotal = importe,
                PagoSimulado = true,
                OrganizadorPersonaId = organizadorPersonaId,
                Categoria = categoria,
                FechaInicio = DateTime.UtcNow.AddDays(30),
                FechaFin = DateTime.UtcNow.AddDays(31),
            });
            return compraId;
        }

        private async Task SeedTicketAsync(string compraId, string eventoId, string ticketTypeId, decimal precioPagado, string ticketTypeNombre = "General")
        {
            var ticket = new Ticket
            {
                Id = Guid.NewGuid().ToString(),
                CompraId = compraId,
                EventoId = eventoId,
                TicketTypeId = ticketTypeId,
                TicketTypeNombre = ticketTypeNombre,
                ClientePersonaId = $"persona-cliente-{Guid.NewGuid():N}",
                PrecioPagado = precioPagado,
                FechaInicio = DateTime.UtcNow.AddDays(30),
                FechaFin = DateTime.UtcNow.AddDays(31),
            };
            await _fixture.Db!.Collection("tickets").Document(ticket.Id).SetAsync(ticket);
        }

        // ---- Ownership del Organizador: la query siempre incluye OrganizadorPersonaId ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_SoloIncluyeComprasPropias_NuncaAjenas()
        {
            var baseFecha = DateTime.UtcNow.AddDays(700);
            var duenoUid = $"uid-dueno-{Guid.NewGuid():N}";
            var duenoPersonaId = $"persona-dueno-{Guid.NewGuid():N}";
            var otroPersonaId = $"persona-otro-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (duenoUid, duenoPersonaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, duenoPersonaId);
            await SeedCompraAsync(eventId, duenoPersonaId, "cliente-1", baseFecha);
            await SeedCompraAsync(eventId, otroPersonaId, "cliente-2", baseFecha);

            var resultado = await sut.GetOrganizerSalesReportAsync(duenoUid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Equal(1, resultado.Resumen.CantidadCompras);
        }

        // ---- Rango: Desde inclusivo, Hasta exclusivo, sobre Compra.FechaCompra (no FechaInicio) ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_DesdeInclusive_HastaExclusive_SobreFechaCompra()
        {
            var baseFecha = DateTime.UtcNow.AddDays(710);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, personaId);

            var desde = baseFecha;
            var hasta = baseFecha.AddDays(3);

            await SeedCompraAsync(eventId, personaId, "c-antes", desde.AddSeconds(-2));
            await SeedCompraAsync(eventId, personaId, "c-desde", desde.AddSeconds(1));
            await SeedCompraAsync(eventId, personaId, "c-antes-hasta", hasta.AddSeconds(-1));
            await SeedCompraAsync(eventId, personaId, "c-hasta", hasta.AddSeconds(1));

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto { FechaDesde = desde, FechaHasta = hasta });

            Assert.Equal(2, resultado.Resumen.CantidadCompras);
        }

        // ---- eventId: propio / ajeno / inexistente ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_EventIdAjeno_ThrowsEventOwnershipException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var otroPersonaId = $"persona-otro-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventId = $"event-ajeno-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, otroPersonaId);

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<EventOwnershipException>(() => sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                EventId = eventId,
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_EventIdInexistente_ThrowsEventNotFoundException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<EventNotFoundException>(() => sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                EventId = $"event-inexistente-{Guid.NewGuid():N}",
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_TicketTypeSinEventId_ThrowsReporteFiltroInvalidoException()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));

            var ahora = DateTime.UtcNow;
            await Assert.ThrowsAsync<ReporteFiltroInvalidoException>(() => sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = ahora.AddDays(-1),
                FechaHasta = ahora.AddDays(300),
                TicketTypeId = "tipo-1",
            }));
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_EventIdPropio_ScopesToThatEvent_AndReturnsPorTipoEntrada()
        {
            var baseFecha = DateTime.UtcNow.AddDays(720);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipoA = new TicketType { Id = $"tipo-a-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
            var eventPropio = $"event-propio-{Guid.NewGuid():N}";
            var eventOtro = $"event-otro-{Guid.NewGuid():N}";
            await SeedEventAsync(eventPropio, personaId, ticketTypes: new List<TicketType> { tipoA });
            await SeedEventAsync(eventOtro, personaId);

            var compraId = await SeedCompraAsync(eventPropio, personaId, "cliente-1", baseFecha, cantidad: 1, importe: 10m);
            await SeedTicketAsync(compraId, eventPropio, tipoA.Id, 10m);
            await SeedCompraAsync(eventOtro, personaId, "cliente-2", baseFecha);

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventPropio,
            });

            Assert.Equal(1, resultado.Resumen.CantidadCompras);
            var tipo = Assert.Single(resultado.PorTipoEntrada);
            Assert.Equal(tipoA.Id, tipo.TicketTypeId);
        }

        // ---- categoria: filtro en memoria ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_FiltraPorCategoria()
        {
            var baseFecha = DateTime.UtcNow.AddDays(730);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, personaId);
            await SeedCompraAsync(eventId, personaId, "c-musica", baseFecha, categoria: Event.EventCategory.Musica);
            await SeedCompraAsync(eventId, personaId, "c-deportes", baseFecha, categoria: Event.EventCategory.Deportes);

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                Categoria = Event.EventCategory.Musica,
            });

            Assert.Equal(1, resultado.Resumen.CantidadCompras);
        }

        // ---- Cero compras ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_CeroCompras_ReturnsEmptyReport()
        {
            var uid = $"uid-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, $"persona-{Guid.NewGuid():N}"));
            var baseFecha = DateTime.UtcNow.AddDays(740);

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha,
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Equal(0, resultado.Resumen.CantidadCompras);
            Assert.Empty(resultado.TopEventos);
        }

        // ---- Más de 30 compras: chunking de WhereIn(CompraId) ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_MasDe30Compras_ChunkingDeTicketsSinPerdidas()
        {
            var baseFecha = DateTime.UtcNow.AddDays(750);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 100 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, personaId, ticketTypes: new List<TicketType> { tipo });

            const int cantidadCompras = 35;
            for (int i = 0; i < cantidadCompras; i++)
            {
                var compraId = await SeedCompraAsync(eventId, personaId, $"cliente-{i}", baseFecha.AddMinutes(i), importe: 10m);
                await SeedTicketAsync(compraId, eventId, tipo.Id, 10m);
            }

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddMinutes(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventId,
            });

            Assert.Equal(cantidadCompras, resultado.Resumen.CantidadCompras);
            var tipoDto = Assert.Single(resultado.PorTipoEntrada);
            Assert.Equal(cantidadCompras, tipoDto.EntradasEmitidas);
            Assert.Equal(cantidadCompras, tipoDto.CantidadComprasDistintas);
        }

        // ---- Tickets legacy sin CompraId: nunca participan del desglose por tipo ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_TicketsLegacySinCompraId_NuncaSeIncluyenEnPorTipoEntrada()
        {
            var baseFecha = DateTime.UtcNow.AddDays(760);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 100 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, personaId, ticketTypes: new List<TicketType> { tipo });

            var compraId = await SeedCompraAsync(eventId, personaId, "cliente-1", baseFecha, importe: 10m);
            await SeedTicketAsync(compraId, eventId, tipo.Id, 10m);

            // Ticket legacy sin CompraId (emitido antes de la etapa "Compra").
            await _fixture.Db!.Collection("tickets").Document($"ticket-legacy-{Guid.NewGuid():N}").SetAsync(new Ticket
            {
                Id = Guid.NewGuid().ToString(),
                CompraId = null,
                EventoId = eventId,
                TicketTypeId = tipo.Id,
                TicketTypeNombre = "General",
                ClientePersonaId = "cliente-legacy",
                PrecioPagado = 10m,
                FechaInicio = DateTime.UtcNow.AddDays(30),
                FechaFin = DateTime.UtcNow.AddDays(31),
            });

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventId,
            });

            var tipoDto = Assert.Single(resultado.PorTipoEntrada);
            Assert.Equal(1, tipoDto.EntradasEmitidas); // solo el ticket con CompraId real, nunca el legacy
        }

        // ---- Admin: sin organizador (global) / con organizador ----

        [FirestoreEmulatorFact]
        public async Task GetAdminSalesReportAsync_SinOrganizador_IncluyeComprasDeCualquierOrganizador()
        {
            var baseFecha = DateTime.UtcNow.AddDays(770);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var organizadorB = $"persona-b-{Guid.NewGuid():N}";
            var eventA = $"event-a-{Guid.NewGuid():N}";
            var eventB = $"event-b-{Guid.NewGuid():N}";
            await SeedEventAsync(eventA, organizadorA);
            await SeedEventAsync(eventB, organizadorB);
            await SeedCompraAsync(eventA, organizadorA, "cliente-1", baseFecha);
            await SeedCompraAsync(eventB, organizadorB, "cliente-2", baseFecha);

            var resultado = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto { FechaDesde = baseFecha.AddDays(-1), FechaHasta = baseFecha.AddDays(1) });

            Assert.Equal(2, resultado.Resumen.CantidadCompras);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminSalesReportAsync_ConOrganizador_ScopesToThatOrganizadorOnly()
        {
            var baseFecha = DateTime.UtcNow.AddDays(780);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var organizadorB = $"persona-b-{Guid.NewGuid():N}";
            var eventA = $"event-a-{Guid.NewGuid():N}";
            var eventB = $"event-b-{Guid.NewGuid():N}";
            await SeedEventAsync(eventA, organizadorA);
            await SeedEventAsync(eventB, organizadorB);
            await SeedCompraAsync(eventA, organizadorA, "cliente-1", baseFecha);
            await SeedCompraAsync(eventB, organizadorB, "cliente-2", baseFecha);

            var resultado = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                OrganizadorPersonaId = organizadorA,
            });

            Assert.Equal(1, resultado.Resumen.CantidadCompras);
        }

        // ---- Compra legacy sin OrganizadorPersonaId/Categoria: se lee segura, nunca rompe el reporte ----

        // ---- FiltrosDisponibles.Eventos: no depende del Top 5 (docs/api-mvp-plan.md §11.11) ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_FiltrosDisponibles_IncludesAllEventsWithCompras_NotOnlyTop5()
        {
            var baseFecha = DateTime.UtcNow.AddDays(800);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            const int cantidadEventos = 8;
            var eventoIds = new List<string>();
            for (int i = 0; i < cantidadEventos; i++)
            {
                var eventId = $"event-{i}-{Guid.NewGuid():N}";
                eventoIds.Add(eventId);
                await SeedEventAsync(eventId, personaId);
                // Importes decrecientes: solo los 5 más caros entran en TopEventos.
                await SeedCompraAsync(eventId, personaId, $"cliente-{i}", baseFecha, importe: (cantidadEventos - i) * 100m);
            }

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Equal(5, resultado.TopEventos.Count); // Top 5 sigue acotado a 5
            Assert.Equal(cantidadEventos, resultado.FiltrosDisponibles.Eventos.Count); // pero las opciones traen los 8

            var idsDisponibles = resultado.FiltrosDisponibles.Eventos.Select(e => e.Id).ToList();
            foreach (var id in eventoIds) Assert.Contains(id, idsDisponibles);

            // El más barato (fuera del Top 5) es filtrable igual: el reporte queda acotado a ese único evento.
            var eventoFueraDelTop5 = eventoIds.Last();
            var filtrado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventoFueraDelTop5,
            });
            Assert.Equal(1, filtrado.Resumen.CantidadCompras);
            Assert.Equal(100m, filtrado.Resumen.ImporteEmitido);

            // Seleccionar un evento no reduce las opciones disponibles: siguen los 8.
            Assert.Equal(cantidadEventos, filtrado.FiltrosDisponibles.Eventos.Count);
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_FiltrosDisponibles_Eventos_AreDeterministic_UniqueAndOrderedByNombreThenId()
        {
            var baseFecha = DateTime.UtcNow.AddDays(810);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventB = $"event-b-{Guid.NewGuid():N}";
            var eventA = $"event-a-{Guid.NewGuid():N}";
            await SeedEventAsync(eventB, personaId);
            await SeedEventAsync(eventA, personaId);

            // Dos Compras del mismo evento: no debe duplicarse en las opciones.
            await SeedCompraAsync(eventB, personaId, "cliente-1", baseFecha, eventoNombre: "Zeta");
            await SeedCompraAsync(eventB, personaId, "cliente-2", baseFecha, eventoNombre: "Zeta");
            await SeedCompraAsync(eventA, personaId, "cliente-3", baseFecha, eventoNombre: "Alfa");

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            Assert.Equal(2, resultado.FiltrosDisponibles.Eventos.Count); // sin duplicados
            Assert.Equal("Alfa", resultado.FiltrosDisponibles.Eventos[0].Nombre); // orden alfabético
            Assert.Equal("Zeta", resultado.FiltrosDisponibles.Eventos[1].Nombre);
        }

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_FiltrosDisponibles_Eventos_RespectsOwnership_NeverAjenos()
        {
            var baseFecha = DateTime.UtcNow.AddDays(820);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var otroPersonaId = $"persona-otro-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var eventPropio = $"event-propio-{Guid.NewGuid():N}";
            var eventAjeno = $"event-ajeno-{Guid.NewGuid():N}";
            await SeedEventAsync(eventPropio, personaId);
            await SeedEventAsync(eventAjeno, otroPersonaId);
            await SeedCompraAsync(eventPropio, personaId, "cliente-1", baseFecha);
            await SeedCompraAsync(eventAjeno, otroPersonaId, "cliente-2", baseFecha);

            var resultado = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });

            var ids = resultado.FiltrosDisponibles.Eventos.Select(e => e.Id).ToList();
            Assert.Contains(eventPropio, ids);
            Assert.DoesNotContain(eventAjeno, ids);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminSalesReportAsync_FiltrosDisponibles_Eventos_RespectsOrganizadorCategoriaYRango()
        {
            var baseFecha = DateTime.UtcNow.AddDays(830);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var organizadorB = $"persona-b-{Guid.NewGuid():N}";
            var eventA = $"event-a-{Guid.NewGuid():N}";
            var eventB = $"event-b-{Guid.NewGuid():N}";
            var eventAFueraDeRango = $"event-a2-{Guid.NewGuid():N}";
            await SeedEventAsync(eventA, organizadorA, categoria: Event.EventCategory.Musica);
            await SeedEventAsync(eventB, organizadorB, categoria: Event.EventCategory.Deportes);
            await SeedEventAsync(eventAFueraDeRango, organizadorA, categoria: Event.EventCategory.Musica);

            await SeedCompraAsync(eventA, organizadorA, "cliente-1", baseFecha, categoria: Event.EventCategory.Musica);
            await SeedCompraAsync(eventB, organizadorB, "cliente-2", baseFecha, categoria: Event.EventCategory.Deportes);
            // Fuera del rango consultado (no debe aparecer en ninguna variante).
            await SeedCompraAsync(eventAFueraDeRango, organizadorA, "cliente-3", baseFecha.AddDays(100), categoria: Event.EventCategory.Musica);

            var porOrganizador = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                OrganizadorPersonaId = organizadorA,
            });
            var idsPorOrganizador = porOrganizador.FiltrosDisponibles.Eventos.Select(e => e.Id).ToList();
            Assert.Contains(eventA, idsPorOrganizador);
            Assert.DoesNotContain(eventB, idsPorOrganizador);
            Assert.DoesNotContain(eventAFueraDeRango, idsPorOrganizador); // fuera de rango

            var porCategoria = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                Categoria = Event.EventCategory.Deportes,
            });
            var idsPorCategoria = porCategoria.FiltrosDisponibles.Eventos.Select(e => e.Id).ToList();
            Assert.Contains(eventB, idsPorCategoria);
            Assert.DoesNotContain(eventA, idsPorCategoria);
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminSalesReportAsync_FiltrosDisponibles_TiposEntrada_AlwaysEmpty_NoTicketTypeIdInContract()
        {
            var baseFecha = DateTime.UtcNow.AddDays(840);
            var sut = CreateSut(_fixture);

            var organizadorA = $"persona-a-{Guid.NewGuid():N}";
            var tipo = new TicketType { Id = $"tipo-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, organizadorA, ticketTypes: new List<TicketType> { tipo });
            var compraId = await SeedCompraAsync(eventId, organizadorA, "cliente-1", baseFecha);
            await SeedTicketAsync(compraId, eventId, tipo.Id, 10m);

            var resultado = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventId,
            });

            Assert.Empty(resultado.FiltrosDisponibles.TiposEntrada);
        }

        // ---- FiltrosDisponibles.TiposEntrada: solo con eventId (Organizador) ----

        [FirestoreEmulatorFact]
        public async Task GetOrganizerSalesReportAsync_FiltrosDisponibles_TiposEntrada_OnlyWithEventId()
        {
            var baseFecha = DateTime.UtcNow.AddDays(850);
            var uid = $"uid-{Guid.NewGuid():N}";
            var personaId = $"persona-{Guid.NewGuid():N}";
            var sut = CreateSut(_fixture, (uid, personaId));

            var tipoA = new TicketType { Id = $"tipo-a-{Guid.NewGuid():N}", Nombre = "VIP", Precio = 50, CantidadDisponible = 5 };
            var tipoB = new TicketType { Id = $"tipo-b-{Guid.NewGuid():N}", Nombre = "General", Precio = 10, CantidadDisponible = 5 };
            var eventId = $"event-{Guid.NewGuid():N}";
            await SeedEventAsync(eventId, personaId, ticketTypes: new List<TicketType> { tipoA, tipoB });

            var compraA = await SeedCompraAsync(eventId, personaId, "cliente-1", baseFecha, importe: 50m);
            await SeedTicketAsync(compraA, eventId, tipoA.Id, 50m, ticketTypeNombre: "VIP");
            var compraB = await SeedCompraAsync(eventId, personaId, "cliente-2", baseFecha, importe: 10m);
            await SeedTicketAsync(compraB, eventId, tipoB.Id, 10m, ticketTypeNombre: "General");

            var sinEventId = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
            });
            Assert.Empty(sinEventId.FiltrosDisponibles.TiposEntrada);

            var conEventId = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventId,
            });
            Assert.Equal(2, conEventId.FiltrosDisponibles.TiposEntrada.Count);
            // Orden por nombre: "General" antes que "VIP".
            Assert.Equal("General", conEventId.FiltrosDisponibles.TiposEntrada[0].Nombre);
            Assert.Equal("VIP", conEventId.FiltrosDisponibles.TiposEntrada[1].Nombre);

            // Aplicar ticketTypeId no vacía las opciones (siguen calculadas antes de ese filtro).
            var conTicketTypeId = await sut.GetOrganizerSalesReportAsync(uid, new VentasOrganizerFilterDto
            {
                FechaDesde = baseFecha.AddDays(-1),
                FechaHasta = baseFecha.AddDays(1),
                EventId = eventId,
                TicketTypeId = tipoB.Id,
            });
            Assert.Equal(2, conTicketTypeId.FiltrosDisponibles.TiposEntrada.Count);
            Assert.Equal(1, conTicketTypeId.Resumen.CantidadCompras); // el resumen sí queda acotado
        }

        [FirestoreEmulatorFact]
        public async Task GetAdminSalesReportAsync_CompraLegacySinFotografiaNueva_IncludedSafely_InSinCategoria()
        {
            var baseFecha = DateTime.UtcNow.AddDays(790);
            var sut = CreateSut(_fixture);

            var compraId = $"compra-legacy-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("compras").Document(compraId).SetAsync(new Dictionary<string, object>
            {
                ["ClientePersonaId"] = "persona-cliente-legacy",
                ["EventoId"] = "event-legacy",
                ["EventoNombre"] = "Evento legacy",
                ["FechaCompra"] = baseFecha,
                ["CantidadEntradas"] = 1,
                ["ImporteTotal"] = 15.0,
                ["PagoSimulado"] = true,
            });

            var resultado = await sut.GetAdminSalesReportAsync(new VentasAdminFilterDto { FechaDesde = baseFecha.AddDays(-1), FechaHasta = baseFecha.AddDays(1) });

            Assert.Equal(1, resultado.Resumen.CantidadCompras);
            var categoria = Assert.Single(resultado.PorCategoria);
            Assert.Equal("Sin categoría", categoria.Categoria);
        }
    }
}
