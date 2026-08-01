using System;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Unitarios de SecurityAdminService con repos mockeados (docs/security-refactor-plan.md §6,
    // Etapa 5): contrato de formato del código de Rol (criterio de revisión #5), 404 de
    // permisos-efectivos para un Usuario inexistente (#3) y el nombre correcto de la auditoría
    // de activación/desactivación de Usuario (#4). La lógica transaccional real (idempotencia,
    // guard del último Administrador) se cubre contra el emulador en
    // Integration/FirestoreRolRepositoryAdminTests y FirestoreUsuarioRepositoryAdminTests.
    public class SecurityAdminServiceTests
    {
        private const string ActorUid = "actor-uid-test";
        private const string ActorUsuarioId = "actor-usuario-id";
        private const string ActorPersonaId = "actor-persona-id";

        private static (SecurityAdminService service, Mock<IRolRepository> rolRepository, Mock<IUsuarioRepository> usuarioRepository, Mock<IAccionRepository> accionRepository, Mock<IPermissionService> permissionService) CreateSut()
        {
            var rolRepository = new Mock<IRolRepository>();
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var permissionService = new Mock<IPermissionService>();

            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ActorUid))
                .ReturnsAsync(ActorUsuarioId);
            usuarioRepository.Setup(r => r.GetByIdAsync(ActorUsuarioId))
                .ReturnsAsync(new Usuario { Id = ActorUsuarioId, PersonaId = ActorPersonaId, IsActive = true });

            var service = new SecurityAdminService(rolRepository.Object, usuarioRepository.Object, accionRepository.Object, permissionService.Object);
            return (service, rolRepository, usuarioRepository, accionRepository, permissionService);
        }

        // ---- #5: formato del código de Rol ----

        [Theory]
        [InlineData("SOPORTE")]
        [InlineData("CONTROL_ACCESO_2")]
        public async Task CrearRolAsync_CodigoValido_NoLanzaArgumentException(string codigo)
        {
            var (service, rolRepository, _, _, _) = CreateSut();
            rolRepository.Setup(r => r.CrearAsync(It.IsAny<Rol>(), It.IsAny<SecurityAudit>())).Returns(Task.CompletedTask);
            rolRepository.Setup(r => r.GetByCodigoAsync(codigo)).ReturnsAsync(new Rol { Codigo = codigo, Nombre = "x" });
            rolRepository.Setup(r => r.GetAccionCodigosAsync(codigo)).ReturnsAsync(Array.Empty<string>());

            var request = new CreateRolRequestDto { Codigo = codigo, Nombre = "Nombre", Descripcion = "x" };

            var resultado = await service.CrearRolAsync(ActorUid, request);

            Assert.Equal(codigo, resultado.Codigo);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("soporte")]
        [InlineData("Soporte")]
        [InlineData("SOPORTE/OTRO")]
        [InlineData("../SOPORTE")]
        [InlineData("SOPORTE OTRO")]
        [InlineData("SOPORTE-2")]
        [InlineData("2SOPORTE")]
        public async Task CrearRolAsync_CodigoInvalido_ThrowsArgumentException(string codigo)
        {
            var (service, _, _, _, _) = CreateSut();
            var request = new CreateRolRequestDto { Codigo = codigo, Nombre = "Nombre", Descripcion = "x" };

            await Assert.ThrowsAsync<ArgumentException>(() => service.CrearRolAsync(ActorUid, request));
        }

        // ---- #3: permisos efectivos de un Usuario inexistente ----

        [Fact]
        public async Task ConsultarPermisosEfectivosAsync_UsuarioInexistente_ThrowsUsuarioNoEncontradoException()
        {
            var (service, _, usuarioRepository, _, permissionService) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("no-existe")).ReturnsAsync((Usuario?)null);

            await Assert.ThrowsAsync<UsuarioNoEncontradoException>(() => service.ConsultarPermisosEfectivosAsync("no-existe"));

            permissionService.Verify(p => p.GetPermisosEfectivosPorUsuarioIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ConsultarPermisosEfectivosAsync_UsuarioDesactivado_Returns200ConUsuarioActivoFalse()
        {
            var (service, _, usuarioRepository, _, permissionService) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-desactivado"))
                .ReturnsAsync(new Usuario { Id = "usuario-desactivado", PersonaId = "persona-x", IsActive = false });
            permissionService.Setup(p => p.GetPermisosEfectivosPorUsuarioIdAsync("usuario-desactivado"))
                .ReturnsAsync(new PermisosEfectivosResult("usuario-desactivado", "persona-x", false, Array.Empty<string>(), Array.Empty<string>()));

            var resultado = await service.ConsultarPermisosEfectivosAsync("usuario-desactivado");

            Assert.False(resultado.UsuarioActivo);
            Assert.Equal("usuario-desactivado", resultado.UsuarioId);
        }

        // ---- #4: nombre de la auditoría de activación/desactivación ----

        [Fact]
        public async Task SetUsuarioActivoAsync_Activar_AuditaComoUsuarioActivar()
        {
            var (service, _, usuarioRepository, _, _) = CreateSut();
            SecurityAudit? auditCapturado = null;
            usuarioRepository.Setup(r => r.SetActivoAsync("usuario-1", true, It.IsAny<SecurityAudit>()))
                .Callback<string, bool, SecurityAudit>((_, _, audit) => auditCapturado = audit)
                .Returns(Task.CompletedTask);

            await service.SetUsuarioActivoAsync(ActorUid, "usuario-1", true);

            Assert.Equal("USUARIO_ACTIVAR", auditCapturado!.Operacion);
        }

        [Fact]
        public async Task SetUsuarioActivoAsync_Desactivar_AuditaComoUsuarioDesactivar()
        {
            var (service, _, usuarioRepository, _, _) = CreateSut();
            SecurityAudit? auditCapturado = null;
            usuarioRepository.Setup(r => r.SetActivoAsync("usuario-1", false, It.IsAny<SecurityAudit>()))
                .Callback<string, bool, SecurityAudit>((_, _, audit) => auditCapturado = audit)
                .Returns(Task.CompletedTask);

            await service.SetUsuarioActivoAsync(ActorUid, "usuario-1", false);

            Assert.Equal("USUARIO_DESACTIVAR", auditCapturado!.Operacion);
        }
    }
}
