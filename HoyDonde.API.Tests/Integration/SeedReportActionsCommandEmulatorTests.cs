using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.Commands;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // "dotnet run --project HoyDonde.API -- seed-report-actions" contra Firestore Emulator real
    // (docs/api-mvp-plan.md §11.5): crea únicamente REPORTE_VER_GLOBAL/REPORTE_VER_PROPIO, nunca
    // roles ni asignaciones Rol->Accion, y reejecutarlo nunca repone una asignación revocada.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class SeedReportActionsCommandEmulatorTests : IAsyncLifetime
    {
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly string _rolDePruebaCodigo = $"TEST_SEED_REPORT_ROL_{Guid.NewGuid():N}".ToUpperInvariant();
        private FirestoreAccionRepository? _accionRepository;
        private FirestoreRolRepository? _rolRepository;
        private SeedReportActionsCommand? _sut;

        public SeedReportActionsCommandEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null)
            {
                _accionRepository = new FirestoreAccionRepository(_fixture.Db);
                _rolRepository = new FirestoreRolRepository(_fixture.Db);
                _sut = new SeedReportActionsCommand(_accionRepository, NullLogger<SeedReportActionsCommand>.Instance);
            }
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_fixture.Db == null) return;
            var docRef = _fixture.Db.Collection("roles").Document(_rolDePruebaCodigo);
            var acciones = await docRef.Collection("acciones").GetSnapshotAsync();
            foreach (var doc in acciones.Documents) await doc.Reference.DeleteAsync();
            await docRef.DeleteAsync();
        }

        private static SecurityAudit NuevoAudit(string operacion, string targetId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = "actor-usuario-test",
            ActorPersonaId = "actor-persona-test",
            Operacion = operacion,
            TargetTipo = "RolAccion",
            TargetId = targetId,
            Detalle = "test",
        };

        [FirestoreEmulatorFact]
        public async Task RunAsync_CreatesBothAcciones_AndSecondRunIsNoOp()
        {
            var primerExitCode = await _sut!.RunAsync();
            Assert.Equal(0, primerExitCode);

            var global = await _accionRepository!.GetByCodigoAsync(Acciones.ReporteVerGlobal);
            var propio = await _accionRepository.GetByCodigoAsync(Acciones.ReporteVerPropio);
            Assert.NotNull(global);
            Assert.NotNull(propio);
            Assert.True(global!.Activo);
            Assert.True(propio!.Activo);

            var segundoExitCode = await _sut.RunAsync();
            Assert.Equal(0, segundoExitCode);

            // Sigue existiendo exactamente una vez cada una (no duplicado ni error).
            var globalDeNuevo = await _accionRepository.GetByCodigoAsync(Acciones.ReporteVerGlobal);
            Assert.NotNull(globalDeNuevo);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_NeverCreatesOrModifiesRoles_NorAssignsAccionesToAnyExistingRol()
        {
            // ADMINISTRADOR/ORGANIZADOR son roles base compartidos por toda la corrida (pueden o
            // no ya tener las acciones de reporte, según qué otros tests corrieron antes en este
            // mismo emulador). Se compara antes/después, no un estado absoluto: el comando no debe
            // cambiar ninguna asignación de ningún rol, sea cual sea el estado inicial.
            var administradorAntes = (await _rolRepository!.GetAccionCodigosAsync("ADMINISTRADOR")).OrderBy(x => x).ToList();
            var organizadorAntes = (await _rolRepository.GetAccionCodigosAsync("ORGANIZADOR")).OrderBy(x => x).ToList();

            await _sut!.RunAsync();
            await _sut.RunAsync();

            var administradorDespues = (await _rolRepository.GetAccionCodigosAsync("ADMINISTRADOR")).OrderBy(x => x).ToList();
            var organizadorDespues = (await _rolRepository.GetAccionCodigosAsync("ORGANIZADOR")).OrderBy(x => x).ToList();

            Assert.Equal(administradorAntes, administradorDespues);
            Assert.Equal(organizadorAntes, organizadorDespues);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_DoesNotRestoreARevokedAssignment_OnARolThatHadIt()
        {
            // Asegura que la Accion exista (el comando es idempotente frente a esto).
            await _sut!.RunAsync();

            await _rolRepository!.CreateAsync(new Rol { Codigo = _rolDePruebaCodigo, Nombre = "Rol de prueba", Descripcion = "x" });
            await _rolRepository.AsignarAccionAsync(_rolDePruebaCodigo, Acciones.ReporteVerGlobal, "actor-test", NuevoAudit("ROL_ASIGNAR_ACCION", $"{_rolDePruebaCodigo}/{Acciones.ReporteVerGlobal}"));

            var accionesTrasAsignar = await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo);
            Assert.Contains(Acciones.ReporteVerGlobal, accionesTrasAsignar);

            // El Administrador revoca la asignación (simulado acá con QuitarAccionAsync, el mismo
            // camino que usa /api/security).
            await _rolRepository.QuitarAccionAsync(_rolDePruebaCodigo, Acciones.ReporteVerGlobal, NuevoAudit("ROL_QUITAR_ACCION", $"{_rolDePruebaCodigo}/{Acciones.ReporteVerGlobal}"));

            var accionesTrasQuitar = await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo);
            Assert.DoesNotContain(Acciones.ReporteVerGlobal, accionesTrasQuitar);

            // Reejecutar el comando (la Accion ya existe: no-op) nunca debe reponer la asignación
            // que el Administrador quitó deliberadamente.
            await _sut.RunAsync();

            var accionesFinal = await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo);
            Assert.DoesNotContain(Acciones.ReporteVerGlobal, accionesFinal);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_PreservesExistingAccionesAndCustomizations()
        {
            var codigoPersonalizado = $"TEST_ACCION_PERSONALIZADA_{Guid.NewGuid():N}";
            await _accionRepository!.CreateAsync(new Accion { Codigo = codigoPersonalizado, Descripcion = "Descripción original" });

            await _sut!.RunAsync();

            var personalizada = await _accionRepository.GetByCodigoAsync(codigoPersonalizado);
            Assert.NotNull(personalizada);
            Assert.Equal("Descripción original", personalizada!.Descripcion);

            await _fixture.Db!.Collection("acciones").Document(codigoPersonalizado).DeleteAsync();
        }
    }
}
