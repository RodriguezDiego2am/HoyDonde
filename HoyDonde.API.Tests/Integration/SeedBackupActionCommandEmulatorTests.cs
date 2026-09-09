using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.Commands;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // "dotnet run --project HoyDonde.API -- seed-backup-action" contra Firestore Emulator real:
    // crea únicamente la Accion BACKUP_ADMINISTRAR, nunca roles ni asignaciones Rol->Accion, y
    // reejecutarlo es un no-op. Mismo criterio que seed-report-actions/seed-role-deletion-action.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class SeedBackupActionCommandEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly FirestoreAccionRepository? _accionRepository;
        private readonly FirestoreRolRepository? _rolRepository;
        private readonly SeedBackupActionCommand? _sut;

        public SeedBackupActionCommandEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db == null) return;
            _accionRepository = new FirestoreAccionRepository(_fixture.Db);
            _rolRepository = new FirestoreRolRepository(_fixture.Db);
            _sut = new SeedBackupActionCommand(_accionRepository, NullLogger<SeedBackupActionCommand>.Instance);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_CreatesOnlyTheAccion_AndSecondRunIsNoOp()
        {
            Assert.Equal(0, await _sut!.RunAsync());

            var accion = await _accionRepository!.GetByCodigoAsync(Acciones.BackupAdministrar);
            Assert.NotNull(accion);
            Assert.True(accion!.Activo);

            // Reejecutar nunca falla ni duplica: el comando es idempotente por contrato.
            Assert.Equal(0, await _sut.RunAsync());
            Assert.NotNull(await _accionRepository.GetByCodigoAsync(Acciones.BackupAdministrar));
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_NeverAssignsTheAccionToAnyRol()
        {
            // El comando crea el Accion y nada más: asignarlo a ADMINISTRADOR es una decisión
            // manual y explícita del operador contra su Firestore real.
            var administradorAntes = (await _rolRepository!.GetAccionCodigosAsync("ADMINISTRADOR")).OrderBy(x => x).ToList();
            var organizadorAntes = (await _rolRepository.GetAccionCodigosAsync("ORGANIZADOR")).OrderBy(x => x).ToList();

            await _sut!.RunAsync();
            await _sut.RunAsync();

            Assert.Equal(administradorAntes, (await _rolRepository.GetAccionCodigosAsync("ADMINISTRADOR")).OrderBy(x => x).ToList());
            Assert.Equal(organizadorAntes, (await _rolRepository.GetAccionCodigosAsync("ORGANIZADOR")).OrderBy(x => x).ToList());
        }
    }
}
