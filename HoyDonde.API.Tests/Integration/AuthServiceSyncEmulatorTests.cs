using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Flujo Cliente de POST /api/auth/sync contra Firestore Emulator real
    // (docs/security-refactor-plan.md §2.1, Etapa 3, criterio de finalización). Ejercita
    // únicamente el aprovisionamiento en Firestore vía IUsuarioRepository real.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class AuthServiceSyncEmulatorTests
    {
        private const string Provider = "FIREBASE";
        private readonly FirestoreEmulatorFixture _fixture;

        public AuthServiceSyncEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        // Acciones (ajuste de contrato backend<->frontend) se resuelven vía la implementación
        // REAL de IPermissionService contra el catálogo real sembrado por SecurityCatalogSeeder
        // -nunca una tabla hardcodeada acá-, igual que en PermissionServiceEmulatorTests.
        private static (FirestoreUsuarioRepository usuarioRepository, AuthService sut) CreateSutAsync(
            FirestoreEmulatorFixture fixture)
        {
            var usuarioRepository = new FirestoreUsuarioRepository(fixture.Db!);
            var rolRepository = new FirestoreRolRepository(fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(fixture.Db!);
            var permissionService = new PermissionService(usuarioRepository, rolRepository, accionRepository);
            var sut = new AuthService(usuarioRepository, permissionService);
            return (usuarioRepository, sut);
        }

        private static SecurityAudit NuevoAudit(string operacion, string targetId) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = "actor-usuario-test",
            ActorPersonaId = "actor-persona-test",
            Operacion = operacion,
            TargetTipo = "Usuario",
            TargetId = targetId,
            Detalle = "test",
        };

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_SecondCall_IsIdempotent_DoesNotDuplicateNorChangeRole()
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            var sut = new AuthService(usuarioRepository, new PermissionService(usuarioRepository, rolRepository, accionRepository));

            var uid = $"uid-{Guid.NewGuid():N}";
            var email = "cliente-sync@test.com";

            var primero = await sut.SyncClienteAsync(uid, email, new SyncClienteRequest("Juan Perez", "12345678", "+5491100000000"));
            Assert.Contains("CLIENTE", primero.Roles);

            // Segundo sync con datos de body distintos: no debe duplicar Persona/Usuario ni
            // cambiar de rol -los datos de body se ignoran por completo en el camino idempotente-.
            var segundo = await sut.SyncClienteAsync(uid, email, new SyncClienteRequest("Otro Nombre", "99999999", "+5499999999"));

            Assert.Equal(primero.UsuarioId, segundo.UsuarioId);
            Assert.Equal(primero.PersonaId, segundo.PersonaId);
            Assert.Equal(new List<string> { "CLIENTE" }, segundo.Roles);

            var usuarioIdResuelto = await usuarioRepository.GetUsuarioIdByExternalSubjectAsync(Provider, uid);
            Assert.Equal(primero.UsuarioId, usuarioIdResuelto);

            var personaSnapshot = await _fixture.Db!.Collection("personas").Document(primero.PersonaId).GetSnapshotAsync();
            Assert.True(personaSnapshot.Exists);
            Assert.Equal("Juan Perez", personaSnapshot.ConvertTo<Models.Persona>().FullName);
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_ForUsuarioWithNonClienteRole_NeverAddsClienteRole()
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var uid = $"uid-{Guid.NewGuid():N}";

            // Simula un Usuario ya provisionado con otro rol (p. ej. Organizador, por
            // UserController) antes de que ese mismo uid llame a /api/auth/sync. Se usa
            // ORGANIZADOR y no ADMINISTRADOR a propósito: BootstrapAdminCommandEmulatorTests
            // hace una collection group query global sobre asignaciones activas de
            // ADMINISTRADOR en el mismo proyecto de emulador compartido por toda la suite, y no
            // debe ver "administradores" fantasma creados por este test.
            await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                $"persona-{Guid.NewGuid():N}", $"usuario-{Guid.NewGuid():N}",
                Provider, uid, "organizador-sync@test.com", "ORGANIZADOR", "actor-1"));

            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            var sut = new AuthService(usuarioRepository, new PermissionService(usuarioRepository, rolRepository, accionRepository));
            var resultado = await sut.SyncClienteAsync(uid, "organizador-sync@test.com", new SyncClienteRequest(null, null, null));

            Assert.DoesNotContain("CLIENTE", resultado.Roles);
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_NewCliente_ReturnsEffectiveAccionesForClienteRole()
        {
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            await new SecurityCatalogSeeder(rolRepository, accionRepository).SeedAsync();

            var (usuarioRepository, sut) = CreateSutAsync(_fixture);
            var uid = $"uid-{Guid.NewGuid():N}";

            var resultado = await sut.SyncClienteAsync(uid, "cliente-acciones@test.com", new SyncClienteRequest(null, null, null));

            Assert.Equal(new List<string> { "TICKET_COMPRAR", "TICKET_VER_PROPIO" }, resultado.Acciones);
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_UsuarioConVariosRoles_ReturnsUnionOfAccionesWithoutDuplicates_Deterministic()
        {
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            await new SecurityCatalogSeeder(rolRepository, accionRepository).SeedAsync();

            var (usuarioRepository, sut) = CreateSutAsync(_fixture);
            var uid = $"uid-{Guid.NewGuid():N}";

            // Primer sync: aprovisiona CLIENTE. Después, el mismo Usuario recibe además el rol
            // ORGANIZADOR (p. ej. otorgado por un Administrador vía /api/security) sin que
            // /api/auth/sync vuelva a tocar sus roles (camino idempotente).
            var primero = await sut.SyncClienteAsync(uid, "multi-rol@test.com", new SyncClienteRequest(null, null, null));
            await usuarioRepository.AsignarRolAsync(primero.UsuarioId, "ORGANIZADOR", "tester",
                NuevoAudit("USUARIO_ASIGNAR_ROL", $"{primero.UsuarioId}/ORGANIZADOR"));

            var segundo = await sut.SyncClienteAsync(uid, "multi-rol@test.com", new SyncClienteRequest(null, null, null));

            // Módulo de reportes (docs/api-mvp-plan.md §11.5): ORGANIZADOR también recibe
            // REPORTE_VER_PROPIO en instalaciones nuevas, intercalado en orden ordinal ascendente.
            var accionesEsperadas = new List<string>
            {
                "CONTROL_CREAR", "EVENTO_CANCELAR_PROPIO", "EVENTO_CREAR", "EVENTO_EDITAR_PROPIO",
                "EVENTO_PUBLICAR_PROPIO", "EVENTO_VER_PROPIOS", "REPORTE_VER_PROPIO", "TICKET_COMPRAR", "TICKET_VER_PROPIO",
            };
            Assert.Equal(accionesEsperadas, segundo.Acciones);
            Assert.Equal(segundo.Acciones.Distinct().Count(), segundo.Acciones.Count);
        }

        [FirestoreEmulatorFact]
        public async Task SyncClienteAsync_UsuarioDesactivado_ReturnsNoAcciones()
        {
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);
            await new SecurityCatalogSeeder(rolRepository, accionRepository).SeedAsync();

            var (usuarioRepository, sut) = CreateSutAsync(_fixture);
            var uid = $"uid-{Guid.NewGuid():N}";

            var primero = await sut.SyncClienteAsync(uid, "desactivado@test.com", new SyncClienteRequest(null, null, null));
            Assert.NotEmpty(primero.Acciones);

            await usuarioRepository.SetActivoAsync(primero.UsuarioId, false,
                NuevoAudit("USUARIO_DESACTIVAR", primero.UsuarioId));

            var segundo = await sut.SyncClienteAsync(uid, "desactivado@test.com", new SyncClienteRequest(null, null, null));

            Assert.Empty(segundo.Acciones);
        }
    }
}
