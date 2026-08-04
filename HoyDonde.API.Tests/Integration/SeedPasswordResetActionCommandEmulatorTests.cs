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
    // "dotnet run --project HoyDonde.API -- seed-password-reset-action" contra Firestore Emulator
    // real (docs/api-mvp-plan.md §13): crea únicamente USUARIO_RESTABLECER_PASSWORD, nunca roles
    // ni asignaciones Rol->Accion, y reejecutarlo nunca repone una asignación revocada.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class SeedPasswordResetActionCommandEmulatorTests : IAsyncLifetime
    {
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly string _rolDePruebaCodigo = $"TEST_SEED_PASSWORD_RESET_ROL_{Guid.NewGuid():N}".ToUpperInvariant();
        private FirestoreAccionRepository? _accionRepository;
        private FirestoreRolRepository? _rolRepository;
        private SeedPasswordResetActionCommand? _sut;

        public SeedPasswordResetActionCommandEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null)
            {
                _accionRepository = new FirestoreAccionRepository(_fixture.Db);
                _rolRepository = new FirestoreRolRepository(_fixture.Db);
                _sut = new SeedPasswordResetActionCommand(_accionRepository, NullLogger<SeedPasswordResetActionCommand>.Instance);
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
        public async Task RunAsync_CreatesUsuarioRestablecerPassword_AndSecondRunIsNoOp()
        {
            var primerExitCode = await _sut!.RunAsync();
            Assert.Equal(0, primerExitCode);

            var accion = await _accionRepository!.GetByCodigoAsync(Acciones.UsuarioRestablecerPassword);
            Assert.NotNull(accion);
            Assert.True(accion!.Activo);

            var segundoExitCode = await _sut.RunAsync();
            Assert.Equal(0, segundoExitCode);

            var accionDeNuevo = await _accionRepository.GetByCodigoAsync(Acciones.UsuarioRestablecerPassword);
            Assert.NotNull(accionDeNuevo);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_NeverCreatesOrModifiesRoles_NorAssignsAccionARolAlguno()
        {
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
            await _sut!.RunAsync();

            await _rolRepository!.CreateAsync(new Rol { Codigo = _rolDePruebaCodigo, Nombre = "Rol de prueba", Descripcion = "x" });
            await _rolRepository.AsignarAccionAsync(_rolDePruebaCodigo, Acciones.UsuarioRestablecerPassword, "actor-test",
                NuevoAudit("ROL_ASIGNAR_ACCION", $"{_rolDePruebaCodigo}/{Acciones.UsuarioRestablecerPassword}"));

            Assert.Contains(Acciones.UsuarioRestablecerPassword, await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo));

            await _rolRepository.QuitarAccionAsync(_rolDePruebaCodigo, Acciones.UsuarioRestablecerPassword,
                NuevoAudit("ROL_QUITAR_ACCION", $"{_rolDePruebaCodigo}/{Acciones.UsuarioRestablecerPassword}"));

            Assert.DoesNotContain(Acciones.UsuarioRestablecerPassword, await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo));

            await _sut.RunAsync();

            Assert.DoesNotContain(Acciones.UsuarioRestablecerPassword, await _rolRepository.GetAccionCodigosAsync(_rolDePruebaCodigo));
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
