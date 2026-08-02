using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // docs/security-refactor-plan.md §4, Etapa 4: control_asignaciones reemplaza los escalares
    // legacy Control.EventId/Control.OrganizadorId. ID determinístico "{ControlPersonaId}_{EventId}",
    // asignación idempotente, y un mismo Control puede tener 2+ asignaciones.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreControlAsignacionRepositoryTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public FirestoreControlAsignacionRepositoryTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAsync_CreatesAsignacion_WithDeterministicId()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventId = $"event-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");

            var snapshot = await _fixture.Db!.Collection("control_asignaciones")
                .Document($"{controlPersonaId}_{eventId}").GetSnapshotAsync();
            Assert.True(snapshot.Exists);

            var asignacion = snapshot.ConvertTo<Models.ControlAsignacion>();
            Assert.Equal(controlPersonaId, asignacion.ControlPersonaId);
            Assert.Equal(eventId, asignacion.EventId);
            Assert.Equal("organizador-persona-1", asignacion.AssignedByPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task AsignarAsync_CalledTwice_IsIdempotent_DoesNotThrowNorDuplicate()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventId = $"event-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1");
            var ex = await Record.ExceptionAsync(() => sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-1"));

            Assert.Null(ex);
            Assert.True(await sut.ExisteAsignacionAsync(controlPersonaId, eventId));
        }

        [FirestoreEmulatorFact]
        public async Task ExisteAsignacionAsync_ReturnsFalse_WhenNeverAssigned()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);

            var existe = await sut.ExisteAsignacionAsync($"control-persona-{Guid.NewGuid():N}", $"event-{Guid.NewGuid():N}");

            Assert.False(existe);
        }

        // Reproduce la condición de carrera de un GetSnapshotAsync + CreateAsync fuera de
        // transacción: N llamadas concurrentes para el mismo (ControlPersonaId, EventId). Con la
        // implementación transaccional (lectura + Create dentro de RunTransactionAsync), Firestore
        // reintenta automáticamente la transacción que pierde la carrera; esa transacción reintentada
        // encuentra el documento ya creado por la ganadora y no vuelve a escribir nada. Ninguna de
        // las N llamadas debe fallar, y el documento final no debe reflejar ninguna escritura
        // posterior a la primera.
        [FirestoreEmulatorFact]
        public async Task AsignarAsync_ConcurrentCallsForSamePair_NoneFail_AndOnlyOneDocumentSurvivesUnmodified()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventId = $"event-{Guid.NewGuid():N}";

            const int concurrency = 8;
            var tasks = Enumerable.Range(0, concurrency)
                .Select(i => Task.Run(() => sut.AsignarAsync(controlPersonaId, eventId, $"organizador-persona-{i}")))
                .ToArray();

            var overallException = await Record.ExceptionAsync(() => Task.WhenAll(tasks));
            Assert.Null(overallException);
            Assert.All(tasks, t => Assert.True(t.IsCompletedSuccessfully));

            var docRef = _fixture.Db!.Collection("control_asignaciones").Document($"{controlPersonaId}_{eventId}");
            var snapshotAfterRace = await docRef.GetSnapshotAsync();
            Assert.True(snapshotAfterRace.Exists);
            var asignacionAfterRace = snapshotAfterRace.ConvertTo<Models.ControlAsignacion>();

            // Una llamada más, después de la carrera: no debe modificar ni AssignedByPersonaId ni
            // CreatedAt del documento que ya ganó la carrera.
            await sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-tardio");

            var snapshotFinal = await docRef.GetSnapshotAsync();
            var asignacionFinal = snapshotFinal.ConvertTo<Models.ControlAsignacion>();

            Assert.Equal(asignacionAfterRace.AssignedByPersonaId, asignacionFinal.AssignedByPersonaId);
            Assert.Equal(asignacionAfterRace.CreatedAt, asignacionFinal.CreatedAt);
            Assert.NotEqual("organizador-persona-tardio", asignacionFinal.AssignedByPersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task SameControlPersonaId_CanHaveTwoOrMoreAsignaciones()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventoA = $"event-a-{Guid.NewGuid():N}";
            var eventoB = $"event-b-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventoA, "organizador-persona-1");
            await sut.AsignarAsync(controlPersonaId, eventoB, "organizador-persona-2");

            Assert.True(await sut.ExisteAsignacionAsync(controlPersonaId, eventoA));
            Assert.True(await sut.ExisteAsignacionAsync(controlPersonaId, eventoB));
        }

        // ---- API-MVP 3: ámbito del organizador y lectura puntual de metadata ----

        [FirestoreEmulatorFact]
        public async Task ExisteAsignacionPorAsignadorAsync_ReturnsTrue_WhenControlWasAssignedByThatOrganizerToAnyEvent()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventoA = $"event-a-{Guid.NewGuid():N}";
            var organizadorPersonaId = $"organizador-persona-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventoA, organizadorPersonaId);

            Assert.True(await sut.ExisteAsignacionPorAsignadorAsync(controlPersonaId, organizadorPersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task ExisteAsignacionPorAsignadorAsync_ReturnsFalse_WhenControlOnlyAssignedByAnotherOrganizer()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventoA = $"event-a-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventoA, "organizador-persona-dueno");

            Assert.False(await sut.ExisteAsignacionPorAsignadorAsync(controlPersonaId, "organizador-persona-otro"));
        }

        [FirestoreEmulatorFact]
        public async Task ExisteAsignacionPorAsignadorAsync_ReturnsFalse_WhenNeverAssigned()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);

            var existe = await sut.ExisteAsignacionPorAsignadorAsync(
                $"control-persona-{Guid.NewGuid():N}", $"organizador-persona-{Guid.NewGuid():N}");

            Assert.False(existe);
        }

        [FirestoreEmulatorFact]
        public async Task GetAsignacionAsync_ReturnsNull_WhenNeverAssigned()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);

            var asignacion = await sut.GetAsignacionAsync($"control-persona-{Guid.NewGuid():N}", $"event-{Guid.NewGuid():N}");

            Assert.Null(asignacion);
        }

        [FirestoreEmulatorFact]
        public async Task GetAsignacionAsync_AfterRepeatedAsignarAsync_PreservesOriginalMetadata()
        {
            var sut = new FirestoreControlAsignacionRepository(_fixture.Db!);
            var controlPersonaId = $"control-persona-{Guid.NewGuid():N}";
            var eventId = $"event-{Guid.NewGuid():N}";

            await sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-original");
            var original = await sut.GetAsignacionAsync(controlPersonaId, eventId);
            Assert.NotNull(original);

            await sut.AsignarAsync(controlPersonaId, eventId, "organizador-persona-tardio");
            var final = await sut.GetAsignacionAsync(controlPersonaId, eventId);

            Assert.NotNull(final);
            Assert.Equal("organizador-persona-original", final!.AssignedByPersonaId);
            Assert.Equal(original!.CreatedAt, final.CreatedAt);
        }
    }
}
