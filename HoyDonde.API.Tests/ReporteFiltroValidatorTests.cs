using System;
using System.Collections.Generic;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Validaciones puras del reporte de eventos (docs/api-mvp-plan.md §11.6/§11.7): rango de
    // fechas, dependencia ticketTypeId->eventId, ownership y filtros en memoria. Todo en memoria,
    // sin Firestore (ver Integration/ReporteServiceEmulatorTests para el recorrido completo).
    public class ReporteFiltroValidatorTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        // ---- ValidateRango ----

        [Fact]
        public void ValidateRango_FechaDesdeAusente_ThrowsReporteRangoInvalidoException()
        {
            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(null, UtcNow));
        }

        [Fact]
        public void ValidateRango_FechaHastaAusente_ThrowsReporteRangoInvalidoException()
        {
            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(UtcNow, null));
        }

        [Fact]
        public void ValidateRango_DesdeMayorOIgualQueHasta_ThrowsReporteRangoInvalidoException()
        {
            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(UtcNow, UtcNow));

            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(UtcNow.AddDays(1), UtcNow));
        }

        [Fact]
        public void ValidateRango_ExcedeMaximoDe366Dias_ThrowsReporteRangoInvalidoException()
        {
            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(UtcNow, UtcNow.AddDays(367)));
        }

        [Fact]
        public void ValidateRango_Exactamente366Dias_Succeeds()
        {
            var (desde, hasta) = ReporteFiltroValidator.ValidateRango(UtcNow, UtcNow.AddDays(366));

            Assert.Equal(UtcNow, desde);
            Assert.Equal(UtcNow.AddDays(366), hasta);
        }

        [Fact]
        public void ValidateRango_FechaSinKindUtc_ThrowsReporteRangoInvalidoException()
        {
            var desdeAmbigua = DateTime.SpecifyKind(UtcNow, DateTimeKind.Unspecified);

            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(desdeAmbigua, UtcNow.AddDays(1)));
        }

        [Fact]
        public void ValidateRango_FechaConKindLocal_ThrowsReporteRangoInvalidoException()
        {
            var desdeLocal = DateTime.SpecifyKind(UtcNow, DateTimeKind.Local);

            Assert.Throws<ReporteRangoInvalidoException>(() =>
                ReporteFiltroValidator.ValidateRango(desdeLocal, UtcNow.AddDays(1)));
        }

        // ---- ValidateTicketTypeRequiresEventId ----

        [Fact]
        public void ValidateTicketTypeRequiresEventId_TicketTypeSinEventId_ThrowsReporteFiltroInvalidoException()
        {
            Assert.Throws<ReporteFiltroInvalidoException>(() =>
                ReporteFiltroValidator.ValidateTicketTypeRequiresEventId(null, "tipo-1"));
        }

        [Fact]
        public void ValidateTicketTypeRequiresEventId_ConEventId_Succeeds()
        {
            var ex = Record.Exception(() => ReporteFiltroValidator.ValidateTicketTypeRequiresEventId("event-1", "tipo-1"));
            Assert.Null(ex);
        }

        [Fact]
        public void ValidateTicketTypeRequiresEventId_SinTicketTypeNiEventId_Succeeds()
        {
            var ex = Record.Exception(() => ReporteFiltroValidator.ValidateTicketTypeRequiresEventId(null, null));
            Assert.Null(ex);
        }

        // ---- EnsureOwnedEvent ----

        [Fact]
        public void EnsureOwnedEvent_EventoInexistente_ThrowsEventNotFoundException()
        {
            Assert.Throws<EventNotFoundException>(() =>
                ReporteFiltroValidator.EnsureOwnedEvent(null, "event-1", "actor-uid", "persona-actor"));
        }

        [Fact]
        public void EnsureOwnedEvent_EventoAjeno_ThrowsEventOwnershipException()
        {
            var evento = new Event { Id = "event-1", OrganizadorPersonaId = "persona-otro" };

            Assert.Throws<EventOwnershipException>(() =>
                ReporteFiltroValidator.EnsureOwnedEvent(evento, "event-1", "actor-uid", "persona-actor"));
        }

        [Fact]
        public void EnsureOwnedEvent_EventoPropio_Succeeds()
        {
            var evento = new Event { Id = "event-1", OrganizadorPersonaId = "persona-actor" };

            var ex = Record.Exception(() => ReporteFiltroValidator.EnsureOwnedEvent(evento, "event-1", "actor-uid", "persona-actor"));

            Assert.Null(ex);
        }

        // ---- EnsureTicketTypeBelongsToEvent ----

        [Fact]
        public void EnsureTicketTypeBelongsToEvent_TicketTypeInexistente_ThrowsTicketTypeInvalidoException()
        {
            var evento = new Event { Id = "event-1", TicketTypes = new List<TicketType> { new() { Id = "tipo-a" } } };

            Assert.Throws<TicketTypeInvalidoException>(() =>
                ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento, "event-1", "tipo-b"));
        }

        [Fact]
        public void EnsureTicketTypeBelongsToEvent_TicketTypeDeOtroEvento_ThrowsTicketTypeInvalidoException()
        {
            // El tipo existe en el catálogo pero pertenece a otro evento: no puede "colarse" acá,
            // porque solo se busca dentro de evento.TicketTypes (nunca una consulta global).
            var evento = new Event { Id = "event-1", TicketTypes = new List<TicketType> { new() { Id = "tipo-de-otro-evento" } } };

            var ex = Record.Exception(() => ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento, "event-1", "tipo-inexistente-en-este-evento"));

            Assert.IsType<TicketTypeInvalidoException>(ex);
        }

        [Fact]
        public void EnsureTicketTypeBelongsToEvent_TicketTypeValido_Succeeds()
        {
            var evento = new Event { Id = "event-1", TicketTypes = new List<TicketType> { new() { Id = "tipo-a" } } };

            var ex = Record.Exception(() => ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento, "event-1", "tipo-a"));

            Assert.Null(ex);
        }

        [Fact]
        public void EnsureTicketTypeBelongsToEvent_SinTicketTypeId_Succeeds()
        {
            var evento = new Event { Id = "event-1", TicketTypes = new List<TicketType>() };

            var ex = Record.Exception(() => ReporteFiltroValidator.EnsureTicketTypeBelongsToEvent(evento, "event-1", null));

            Assert.Null(ex);
        }

        // ---- CumpleFiltros ----

        private static Event BuildEvento(DateTime fechaInicio, Event.EventStatus estado = Event.EventStatus.Publicado, Event.EventCategory categoria = Event.EventCategory.Musica, DateTime? fechaFin = null) => new()
        {
            Id = "event-1",
            FechaInicio = fechaInicio,
            FechaFin = fechaFin ?? fechaInicio.AddDays(1),
            Estado = estado,
            Categoria = categoria,
        };

        [Fact]
        public void CumpleFiltros_FechaInicioFueraDeRango_ReturnsFalse()
        {
            var desde = UtcNow;
            var hasta = UtcNow.AddDays(3);

            var antesDeRango = BuildEvento(desde.AddSeconds(-1));
            var enHasta = BuildEvento(hasta); // Hasta es exclusiva
            var dentro = BuildEvento(desde.AddHours(1));

            var filter = new ReporteEventosFilterDto();

            Assert.False(ReporteFiltroValidator.CumpleFiltros(antesDeRango, filter, desde, hasta, UtcNow));
            Assert.False(ReporteFiltroValidator.CumpleFiltros(enHasta, filter, desde, hasta, UtcNow));
            Assert.True(ReporteFiltroValidator.CumpleFiltros(dentro, filter, desde, hasta, UtcNow));
        }

        [Fact]
        public void CumpleFiltros_DesdeInclusive_ReturnsTrue()
        {
            var desde = UtcNow;
            var hasta = UtcNow.AddDays(3);
            var justoEnDesde = BuildEvento(desde);

            Assert.True(ReporteFiltroValidator.CumpleFiltros(justoEnDesde, new ReporteEventosFilterDto(), desde, hasta, UtcNow));
        }

        [Fact]
        public void CumpleFiltros_EstadoNoCoincide_ReturnsFalse()
        {
            var evento = BuildEvento(UtcNow, estado: Event.EventStatus.Borrador);
            var filter = new ReporteEventosFilterDto { Estado = Event.EventEffectiveStatus.Publicado };

            Assert.False(ReporteFiltroValidator.CumpleFiltros(evento, filter, UtcNow.AddDays(-1), UtcNow.AddDays(1), UtcNow));
        }

        [Fact]
        public void CumpleFiltros_EstadoFinalizado_DistinguidoDePublicado()
        {
            var vigente = BuildEvento(UtcNow.AddDays(-2), estado: Event.EventStatus.Publicado, fechaFin: UtcNow.AddDays(1));
            var finalizado = BuildEvento(UtcNow.AddDays(-2), estado: Event.EventStatus.Publicado, fechaFin: UtcNow.AddDays(-1));

            var filterPublicado = new ReporteEventosFilterDto { Estado = Event.EventEffectiveStatus.Publicado };
            var filterFinalizado = new ReporteEventosFilterDto { Estado = Event.EventEffectiveStatus.Finalizado };

            var desde = UtcNow.AddDays(-5);
            var hasta = UtcNow.AddDays(5);

            Assert.True(ReporteFiltroValidator.CumpleFiltros(vigente, filterPublicado, desde, hasta, UtcNow));
            Assert.False(ReporteFiltroValidator.CumpleFiltros(vigente, filterFinalizado, desde, hasta, UtcNow));

            Assert.False(ReporteFiltroValidator.CumpleFiltros(finalizado, filterPublicado, desde, hasta, UtcNow));
            Assert.True(ReporteFiltroValidator.CumpleFiltros(finalizado, filterFinalizado, desde, hasta, UtcNow));
        }

        [Fact]
        public void CumpleFiltros_CategoriaNoCoincide_ReturnsFalse()
        {
            var evento = BuildEvento(UtcNow, categoria: Event.EventCategory.Deportes);
            var filter = new ReporteEventosFilterDto { Categoria = Event.EventCategory.Musica };

            Assert.False(ReporteFiltroValidator.CumpleFiltros(evento, filter, UtcNow.AddDays(-1), UtcNow.AddDays(1), UtcNow));
        }

        [Fact]
        public void CumpleFiltros_SinFiltrosOpcionales_SoloDependeDeRango()
        {
            var evento = BuildEvento(UtcNow, estado: Event.EventStatus.Cancelado, categoria: Event.EventCategory.Otros);
            var filter = new ReporteEventosFilterDto();

            Assert.True(ReporteFiltroValidator.CumpleFiltros(evento, filter, UtcNow.AddDays(-1), UtcNow.AddDays(1), UtcNow));
        }
    }
}
