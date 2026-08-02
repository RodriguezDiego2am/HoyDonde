using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // API-MVP 5 (docs/api-mvp-plan.md §6) ejercitado contra Firestore Emulator real:
    // UserService.ListarControlesDelOrganizadorAsync/ListarControlesDelEventoAsync/
    // ListarEventosAsignadosAsync con FirestoreUsuarioRepository/FirestoreControlAsignacionRepository/
    // EventService reales. Mismo patrón que UserServiceControlAssignmentEmulatorTests (API-MVP 3):
    // IIdentityProvider/IIdentidadHuerfanaRepository quedan mockeados, ninguna de estas consultas
    // de solo lectura debe tocarlos.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class UserServiceOperationalQueriesEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public UserServiceOperationalQueriesEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private UserService CreateSut(params (string uid, string personaId)[] actors)
        {
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>();
            foreach (var (uid, personaId) in actors)
            {
                personaResolver.Setup(r => r.ResolvePersonaIdAsync(uid)).ReturnsAsync(personaId);
            }

            var eventService = new EventService(_fixture.Db!, personaResolver.Object, Mock.Of<ILogger<EventService>>());
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);

            return new UserService(
                usuarioRepository,
                Mock.Of<IIdentidadHuerfanaRepository>(),
                eventService,
                Mock.Of<IIdentityProvider>(),
                personaResolver.Object,
                controlAsignacionRepository,
                Mock.Of<ILogger<UserService>>());
        }

        private async Task<Event> SeedEventAsync(
            string organizadorPersonaId,
            string? nombre = null,
            Event.EventStatus estado = Event.EventStatus.Borrador,
            DateTime? fechaInicio = null,
            DateTime? fechaFin = null)
        {
            var evento = new Event
            {
                Id = $"event-{Guid.NewGuid():N}",
                Nombre = nombre ?? "Evento de prueba",
                Descripcion = "Descripcion",
                Ubicacion = "Buenos Aires",
                Categoria = Event.EventCategory.Musica,
                FechaInicio = fechaInicio ?? DateTime.UtcNow.AddDays(5),
                FechaFin = fechaFin ?? DateTime.UtcNow.AddDays(6),
                OrganizadorPersonaId = organizadorPersonaId,
                Estado = estado,
            };
            await _fixture.Db!.Collection("events").Document(evento.Id).SetAsync(evento);
            return evento;
        }

        private async Task<string> SeedControlUsuarioAsync(string personaId, string? userName = null, bool activo = true)
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var usuarioId = $"usuario-{Guid.NewGuid():N}";
            var email = $"{userName ?? personaId}@control.hoydonde.com";
            await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, "FIREBASE", $"uid-control-{Guid.NewGuid():N}", email, "CONTROL", "test"));

            if (!activo)
            {
                await _fixture.Db!.Collection("usuarios").Document(usuarioId).UpdateAsync(nameof(Usuario.IsActive), false);
            }

            return usuarioId;
        }

        // ---- ListarControlesDelOrganizadorAsync ----

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelOrganizadorAsync_ReturnsDistinctControls_AcrossMultipleOwnEvents()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var eventoA = await SeedEventAsync(organizadorPersonaId);
            var eventoB = await SeedEventAsync(organizadorPersonaId);
            var eventoC = await SeedEventAsync(organizadorPersonaId);
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            await SeedControlUsuarioAsync(controlPersonaId, userName: "control-repetido");

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoA.Id, organizadorPersonaId);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoB.Id, organizadorPersonaId);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoC.Id, organizadorPersonaId);

            var resultado = await sut.ListarControlesDelOrganizadorAsync(organizadorUid);

            var soloEste = resultado.Where(c => c.ControlPersonaId == controlPersonaId).ToList();
            Assert.Single(soloEste);
            Assert.Equal("control-repetido", soloEste[0].UserName);
            Assert.True(soloEste[0].Activo);
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelOrganizadorAsync_ExcludesControlsAssignedOnlyByAnotherOrganizer()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var eventoAjeno = await SeedEventAsync($"persona-otro-org-{Guid.NewGuid():N}");
            var controlAjeno = $"persona-control-{Guid.NewGuid():N}";
            await SeedControlUsuarioAsync(controlAjeno);

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlAjeno, eventoAjeno.Id, "persona-otro-organizador");

            var resultado = await sut.ListarControlesDelOrganizadorAsync(organizadorUid);

            Assert.DoesNotContain(resultado, c => c.ControlPersonaId == controlAjeno);
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelOrganizadorAsync_ReturnsEmpty_WhenOrganizerNeverAssignedAnyControl()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var resultado = await sut.ListarControlesDelOrganizadorAsync(organizadorUid);

            Assert.Empty(resultado);
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelOrganizadorAsync_IncludesInactiveControl_WithActivoFalse()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            await SeedControlUsuarioAsync(controlPersonaId, activo: false);

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, evento.Id, organizadorPersonaId);

            var resultado = await sut.ListarControlesDelOrganizadorAsync(organizadorUid);

            var control = Assert.Single(resultado, c => c.ControlPersonaId == controlPersonaId);
            Assert.False(control.Activo);
        }

        // ---- ListarControlesDelEventoAsync ----

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelEventoAsync_ReturnsControlsAssignedToThatEvent_WithAssignmentMetadata()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);
            var otroEvento = await SeedEventAsync(organizadorPersonaId);
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            await SeedControlUsuarioAsync(controlPersonaId, userName: "control-evento");

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, evento.Id, organizadorPersonaId);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, otroEvento.Id, organizadorPersonaId);

            var resultado = await sut.ListarControlesDelEventoAsync(organizadorUid, evento.Id);

            var control = Assert.Single(resultado);
            Assert.Equal(controlPersonaId, control.ControlPersonaId);
            Assert.Equal("control-evento", control.UserName);
            Assert.True(control.Activo);
            Assert.Equal(organizadorPersonaId, control.AssignedByPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelEventoAsync_ForeignEvent_ThrowsOwnershipException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var eventoAjeno = await SeedEventAsync($"persona-otro-org-{Guid.NewGuid():N}");

            await Assert.ThrowsAsync<EventOwnershipException>(
                () => sut.ListarControlesDelEventoAsync(organizadorUid, eventoAjeno.Id));
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelEventoAsync_NonexistentEvent_ThrowsNotFoundException()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            await Assert.ThrowsAsync<EventNotFoundException>(
                () => sut.ListarControlesDelEventoAsync(organizadorUid, $"event-inexistente-{Guid.NewGuid():N}"));
        }

        [FirestoreEmulatorFact]
        public async Task ListarControlesDelEventoAsync_EventWithNoControls_ReturnsEmpty()
        {
            var organizadorUid = $"uid-org-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";
            var sut = CreateSut((organizadorUid, organizadorPersonaId));

            var evento = await SeedEventAsync(organizadorPersonaId);

            var resultado = await sut.ListarControlesDelEventoAsync(organizadorUid, evento.Id);

            Assert.Empty(resultado);
        }

        // ---- ListarEventosAsignadosAsync ----

        [FirestoreEmulatorFact]
        public async Task ListarEventosAsignadosAsync_ReturnsAllAssignedEvents_ForThatControl_WithoutDuplicates()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut((controlUid, controlPersonaId));

            var eventoA = await SeedEventAsync($"persona-org-{Guid.NewGuid():N}", nombre: "Evento A", fechaInicio: DateTime.UtcNow.AddDays(3), fechaFin: DateTime.UtcNow.AddDays(4));
            var eventoB = await SeedEventAsync($"persona-org-{Guid.NewGuid():N}", nombre: "Evento B", fechaInicio: DateTime.UtcNow.AddDays(1), fechaFin: DateTime.UtcNow.AddDays(2));

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoA.Id, "persona-org-1");
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventoB.Id, "persona-org-2");

            var resultado = await sut.ListarEventosAsignadosAsync(controlUid);

            Assert.Equal(2, resultado.Count);
            Assert.Equal(new[] { eventoB.Id, eventoA.Id }, resultado.Select(e => e.EventId));
        }

        [FirestoreEmulatorFact]
        public async Task ListarEventosAsignadosAsync_ExcludesEventsAssignedToAnotherControl()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut((controlUid, controlPersonaId));

            var eventoDeOtroControl = await SeedEventAsync($"persona-org-{Guid.NewGuid():N}");
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            await controlAsignacionRepository.AsignarAsync($"persona-otro-control-{Guid.NewGuid():N}", eventoDeOtroControl.Id, "persona-org-1");

            var resultado = await sut.ListarEventosAsignadosAsync(controlUid);

            Assert.Empty(resultado);
        }

        [FirestoreEmulatorFact]
        public async Task ListarEventosAsignadosAsync_ReturnsEmpty_WhenControlHasNoAsignaciones()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut((controlUid, controlPersonaId));

            var resultado = await sut.ListarEventosAsignadosAsync(controlUid);

            Assert.Empty(resultado);
        }

        [FirestoreEmulatorFact]
        public async Task ListarEventosAsignadosAsync_ProjectsEffectiveStatus_ForAllFourStates()
        {
            var controlUid = $"uid-control-{Guid.NewGuid():N}";
            var controlPersonaId = $"persona-control-{Guid.NewGuid():N}";
            var sut = CreateSut((controlUid, controlPersonaId));
            var organizadorPersonaId = $"persona-org-{Guid.NewGuid():N}";

            var borrador = await SeedEventAsync(organizadorPersonaId, estado: Event.EventStatus.Borrador);
            var publicado = await SeedEventAsync(organizadorPersonaId, estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddHours(1), fechaFin: DateTime.UtcNow.AddHours(2));
            var cancelado = await SeedEventAsync(organizadorPersonaId, estado: Event.EventStatus.Cancelado);
            var finalizado = await SeedEventAsync(organizadorPersonaId, estado: Event.EventStatus.Publicado,
                fechaInicio: DateTime.UtcNow.AddDays(-2), fechaFin: DateTime.UtcNow.AddDays(-1));

            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(_fixture.Db!);
            foreach (var evento in new[] { borrador, publicado, cancelado, finalizado })
            {
                await controlAsignacionRepository.AsignarAsync(controlPersonaId, evento.Id, organizadorPersonaId);
            }

            var resultado = await sut.ListarEventosAsignadosAsync(controlUid);

            Assert.Equal(Event.EventEffectiveStatus.Borrador, resultado.Single(e => e.EventId == borrador.Id).Estado);
            Assert.Equal(Event.EventEffectiveStatus.Publicado, resultado.Single(e => e.EventId == publicado.Id).Estado);
            Assert.Equal(Event.EventEffectiveStatus.Cancelado, resultado.Single(e => e.EventId == cancelado.Id).Estado);
            Assert.Equal(Event.EventEffectiveStatus.Finalizado, resultado.Single(e => e.EventId == finalizado.Id).Estado);
        }
    }
}
