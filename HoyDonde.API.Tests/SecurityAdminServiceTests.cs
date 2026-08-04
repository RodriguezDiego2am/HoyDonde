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

        private static (SecurityAdminService service, Mock<IRolRepository> rolRepository, Mock<IUsuarioRepository> usuarioRepository, Mock<IAccionRepository> accionRepository, Mock<IPermissionService> permissionService, Mock<IIdentityProvider> identityProvider, Mock<ISecurityAuditRepository> securityAuditRepository) CreateSut()
        {
            var rolRepository = new Mock<IRolRepository>();
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var permissionService = new Mock<IPermissionService>();
            var identityProvider = new Mock<IIdentityProvider>();
            var securityAuditRepository = new Mock<ISecurityAuditRepository>();

            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, ActorUid))
                .ReturnsAsync(ActorUsuarioId);
            usuarioRepository.Setup(r => r.GetByIdAsync(ActorUsuarioId))
                .ReturnsAsync(new Usuario { Id = ActorUsuarioId, PersonaId = ActorPersonaId, IsActive = true });

            var service = new SecurityAdminService(
                rolRepository.Object, usuarioRepository.Object, accionRepository.Object, permissionService.Object,
                identityProvider.Object, securityAuditRepository.Object);
            return (service, rolRepository, usuarioRepository, accionRepository, permissionService, identityProvider, securityAuditRepository);
        }

        // ---- #5: formato del código de Rol ----

        [Theory]
        [InlineData("SOPORTE")]
        [InlineData("CONTROL_ACCESO_2")]
        public async Task CrearRolAsync_CodigoValido_NoLanzaArgumentException(string codigo)
        {
            var (service, rolRepository, _, _, _, _, _) = CreateSut();
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
            var (service, _, _, _, _, _, _) = CreateSut();
            var request = new CreateRolRequestDto { Codigo = codigo, Nombre = "Nombre", Descripcion = "x" };

            await Assert.ThrowsAsync<ArgumentException>(() => service.CrearRolAsync(ActorUid, request));
        }

        // ---- #3: permisos efectivos de un Usuario inexistente ----

        [Fact]
        public async Task ConsultarPermisosEfectivosAsync_UsuarioInexistente_ThrowsUsuarioNoEncontradoException()
        {
            var (service, _, usuarioRepository, _, permissionService, _, _) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("no-existe")).ReturnsAsync((Usuario?)null);

            await Assert.ThrowsAsync<UsuarioNoEncontradoException>(() => service.ConsultarPermisosEfectivosAsync("no-existe"));

            permissionService.Verify(p => p.GetPermisosEfectivosPorUsuarioIdAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task ConsultarPermisosEfectivosAsync_UsuarioDesactivado_Returns200ConUsuarioActivoFalse()
        {
            var (service, _, usuarioRepository, _, permissionService, _, _) = CreateSut();
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
            var (service, _, usuarioRepository, _, _, _, _) = CreateSut();
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
            var (service, _, usuarioRepository, _, _, _, _) = CreateSut();
            SecurityAudit? auditCapturado = null;
            usuarioRepository.Setup(r => r.SetActivoAsync("usuario-1", false, It.IsAny<SecurityAudit>()))
                .Callback<string, bool, SecurityAudit>((_, _, audit) => auditCapturado = audit)
                .Returns(Task.CompletedTask);

            await service.SetUsuarioActivoAsync(ActorUid, "usuario-1", false);

            Assert.Equal("USUARIO_DESACTIVAR", auditCapturado!.Operacion);
        }

        // ---- Baja física de un Rol (docs/api-mvp-plan.md §12) ----

        [Fact]
        public async Task EliminarRolAsync_AuditaComoRolEliminar_AndDelegatesToRepository()
        {
            var (service, rolRepository, _, _, _, _, _) = CreateSut();
            SecurityAudit? auditCapturado = null;
            rolRepository.Setup(r => r.EliminarAsync("SOPORTE", It.IsAny<SecurityAudit>()))
                .Callback<string, SecurityAudit>((_, audit) => auditCapturado = audit)
                .Returns(Task.CompletedTask);

            await service.EliminarRolAsync(ActorUid, "SOPORTE");

            Assert.Equal("ROL_ELIMINAR", auditCapturado!.Operacion);
            Assert.Equal("Rol", auditCapturado.TargetTipo);
            Assert.Equal("SOPORTE", auditCapturado.TargetId);
            rolRepository.Verify(r => r.EliminarAsync("SOPORTE", It.IsAny<SecurityAudit>()), Times.Once);
        }

        [Fact]
        public async Task EliminarRolAsync_PropagaExcepcionDelRepositorio()
        {
            var (service, rolRepository, _, _, _, _, _) = CreateSut();
            rolRepository.Setup(r => r.EliminarAsync("ORGANIZADOR", It.IsAny<SecurityAudit>()))
                .ThrowsAsync(new RolProtegidoException("ORGANIZADOR"));

            await Assert.ThrowsAsync<RolProtegidoException>(() => service.EliminarRolAsync(ActorUid, "ORGANIZADOR"));
        }

        // ---- Enlace de recuperación de contraseña (docs/api-mvp-plan.md §13) ----

        [Fact]
        public async Task GenerarPasswordResetLinkAsync_UsuarioInexistente_ThrowsUsuarioNoEncontradoException()
        {
            var (service, _, usuarioRepository, _, _, identityProvider, securityAuditRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("no-existe")).ReturnsAsync((Usuario?)null);

            await Assert.ThrowsAsync<UsuarioNoEncontradoException>(() => service.GenerarPasswordResetLinkAsync(ActorUid, "no-existe"));

            identityProvider.Verify(p => p.GeneratePasswordResetLinkAsync(It.IsAny<string>()), Times.Never);
            securityAuditRepository.Verify(r => r.RegistrarAsync(It.IsAny<SecurityAudit>()), Times.Never);
        }

        [Theory]
        [InlineData("", FirebaseIdentityProvider.ProviderName)]
        [InlineData("uid-firebase-1", "OTRO_PROVEEDOR")]
        public async Task GenerarPasswordResetLinkAsync_UsuarioSinIdentidadFirebaseRecuperable_ThrowsTipada(
            string externalSubjectId, string identityProvider_)
        {
            var (service, _, usuarioRepository, _, _, identityProvider, securityAuditRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario
            {
                Id = "usuario-1",
                PersonaId = "persona-1",
                IsActive = true,
                IdentityProvider = identityProvider_,
                ExternalSubjectId = externalSubjectId,
                Email = "usuario1@hoydonde.com",
            });

            await Assert.ThrowsAsync<UsuarioSinIdentidadRecuperableException>(() => service.GenerarPasswordResetLinkAsync(ActorUid, "usuario-1"));

            identityProvider.Verify(p => p.GeneratePasswordResetLinkAsync(It.IsAny<string>()), Times.Never);
            securityAuditRepository.Verify(r => r.RegistrarAsync(It.IsAny<SecurityAudit>()), Times.Never);
        }

        [Fact]
        public async Task GenerarPasswordResetLinkAsync_Exito_InvocaProveedorConElExternalSubjectIdDelUsuario_YDevuelveSoloElEnlace()
        {
            var (service, _, usuarioRepository, _, _, identityProvider, securityAuditRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario
            {
                Id = "usuario-1",
                PersonaId = "persona-1",
                IsActive = true,
                IdentityProvider = FirebaseIdentityProvider.ProviderName,
                ExternalSubjectId = "uid-firebase-real",
                Email = "usuario1@hoydonde.com",
            });
            identityProvider.Setup(p => p.GeneratePasswordResetLinkAsync("uid-firebase-real"))
                .ReturnsAsync("https://firebase.example/__/auth/action?mode=resetPassword&oobCode=abc123");

            var resultado = await service.GenerarPasswordResetLinkAsync(ActorUid, "usuario-1");

            Assert.Equal("https://firebase.example/__/auth/action?mode=resetPassword&oobCode=abc123", resultado.ResetLink);
            identityProvider.Verify(p => p.GeneratePasswordResetLinkAsync("uid-firebase-real"), Times.Once);
        }

        [Fact]
        public async Task GenerarPasswordResetLinkAsync_Exito_AuditaSinEmailNiEnlace()
        {
            var (service, _, usuarioRepository, _, _, identityProvider, securityAuditRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario
            {
                Id = "usuario-1",
                PersonaId = "persona-1",
                IsActive = true,
                IdentityProvider = FirebaseIdentityProvider.ProviderName,
                ExternalSubjectId = "uid-firebase-real",
                Email = "usuario1@hoydonde.com",
            });
            const string enlaceSecreto = "https://firebase.example/__/auth/action?mode=resetPassword&oobCode=abc123";
            identityProvider.Setup(p => p.GeneratePasswordResetLinkAsync("uid-firebase-real")).ReturnsAsync(enlaceSecreto);

            SecurityAudit? auditCapturado = null;
            securityAuditRepository.Setup(r => r.RegistrarAsync(It.IsAny<SecurityAudit>()))
                .Callback<SecurityAudit>(audit => auditCapturado = audit)
                .Returns(Task.CompletedTask);

            await service.GenerarPasswordResetLinkAsync(ActorUid, "usuario-1");

            securityAuditRepository.Verify(r => r.RegistrarAsync(It.IsAny<SecurityAudit>()), Times.Once);
            Assert.NotNull(auditCapturado);
            Assert.Equal("USUARIO_GENERAR_RESET_PASSWORD", auditCapturado!.Operacion);
            Assert.Equal("Usuario", auditCapturado.TargetTipo);
            Assert.Equal("usuario-1", auditCapturado.TargetId);
            Assert.Equal(ActorUsuarioId, auditCapturado.ActorUsuarioId);
            Assert.DoesNotContain("usuario1@hoydonde.com", auditCapturado.Detalle);
            Assert.DoesNotContain(enlaceSecreto, auditCapturado.Detalle);
            Assert.DoesNotContain("firebase.example", auditCapturado.Detalle);
        }

        [Fact]
        public async Task GenerarPasswordResetLinkAsync_FalloDelProveedor_NuncaEscribeAuditoria()
        {
            var (service, _, usuarioRepository, _, _, identityProvider, securityAuditRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario
            {
                Id = "usuario-1",
                PersonaId = "persona-1",
                IsActive = true,
                IdentityProvider = FirebaseIdentityProvider.ProviderName,
                ExternalSubjectId = "uid-firebase-real",
                Email = "usuario1@hoydonde.com",
            });
            identityProvider.Setup(p => p.GeneratePasswordResetLinkAsync("uid-firebase-real"))
                .ThrowsAsync(new InvalidOperationException("fallo simulado del proveedor de identidad"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerarPasswordResetLinkAsync(ActorUid, "usuario-1"));

            securityAuditRepository.Verify(r => r.RegistrarAsync(It.IsAny<SecurityAudit>()), Times.Never);
        }
    }
}
