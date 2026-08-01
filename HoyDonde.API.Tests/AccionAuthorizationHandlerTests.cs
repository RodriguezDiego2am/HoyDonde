using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Cubre AccionAuthorizationHandler de punta a punta contra una PermissionService REAL
    // (repos mockeados, mismo patrón que PermissionServiceTests) para probar que el handler
    // nunca confía en el claim legacy "role" y que la decisión sale exclusivamente de
    // Usuario -> UsuarioRol -> Rol -> RolAccion -> Accion (docs/security-refactor-plan.md §3,
    // Etapa 5).
    public class AccionAuthorizationHandlerTests
    {
        private const string Provider = "FIREBASE";
        private const string Uid = "uid-handler-test";
        private const string Accion = "TICKET_VALIDAR";

        private static (AccionAuthorizationHandler handler, Mock<IUsuarioRepository> usuarioRepository, Mock<IRolRepository> rolRepository, Mock<IAccionRepository> accionRepository) CreateSut()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var rolRepository = new Mock<IRolRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var permissionService = new PermissionService(usuarioRepository.Object, rolRepository.Object, accionRepository.Object);
            var handler = new AccionAuthorizationHandler(permissionService, NullLogger<AccionAuthorizationHandler>.Instance);
            return (handler, usuarioRepository, rolRepository, accionRepository);
        }

        private static AuthorizationHandlerContext BuildContext(AccionRequirement requirement, ClaimsPrincipal principal) =>
            new(new[] { requirement }, principal, null);

        private static ClaimsPrincipal PrincipalConUid(string? uid, string? legacyRoleClaim = null)
        {
            var claims = new List<Claim>();
            if (uid != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, uid));
            if (legacyRoleClaim != null) claims.Add(new Claim("role", legacyRoleClaim));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        }

        private static void SetupUsuarioConRoles(
            Mock<IUsuarioRepository> usuarioRepository, Mock<IRolRepository> rolRepository, Mock<IAccionRepository> accionRepository,
            string usuarioId, bool usuarioActivo, IReadOnlyDictionary<string, (bool RolActivo, string[] Acciones)> rolesConAcciones)
        {
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, Uid)).ReturnsAsync(usuarioId);
            usuarioRepository.Setup(r => r.GetByIdAsync(usuarioId)).ReturnsAsync(new Usuario { Id = usuarioId, PersonaId = "persona-1", IsActive = usuarioActivo });
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync(usuarioId)).ReturnsAsync(new List<string>(rolesConAcciones.Keys));

            foreach (var (rolCodigo, (rolActivo, acciones)) in rolesConAcciones)
            {
                rolRepository.Setup(r => r.GetByCodigoAsync(rolCodigo)).ReturnsAsync(new Rol { Codigo = rolCodigo, Activo = rolActivo });
                rolRepository.Setup(r => r.GetAccionCodigosAsync(rolCodigo)).ReturnsAsync(acciones);
                foreach (var accionCodigo in acciones)
                {
                    accionRepository.Setup(r => r.GetByCodigoAsync(accionCodigo)).ReturnsAsync(new Accion { Codigo = accionCodigo, Activo = true });
                }
            }
        }

        [Fact]
        public async Task AccionEfectiva_Autoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])> { ["CONTROL"] = (true, new[] { Accion }) });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task IdentidadInexistente_NoAutoriza()
        {
            var (handler, _, _, _) = CreateSut();
            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(null));

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task UsuarioInactivo_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", false,
                new Dictionary<string, (bool, string[])> { ["CONTROL"] = (true, new[] { Accion }) });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task UsuarioRolInactivo_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, Uid)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = true });
            // La asignación UsuarioRol está inactiva: GetRolCodigosActivosAsync (que ya filtra
            // Activo == true) no la incluye.
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync("usuario-1")).ReturnsAsync(new List<string>());

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
            rolRepository.Verify(r => r.GetByCodigoAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task RolInactivo_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])> { ["CONTROL"] = (false, new[] { Accion }) });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task AccionInactiva_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, Uid)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = true });
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync("usuario-1")).ReturnsAsync(new List<string> { "CONTROL" });
            rolRepository.Setup(r => r.GetByCodigoAsync("CONTROL")).ReturnsAsync(new Rol { Codigo = "CONTROL", Activo = true });
            rolRepository.Setup(r => r.GetAccionCodigosAsync("CONTROL")).ReturnsAsync(new List<string> { Accion });
            accionRepository.Setup(r => r.GetByCodigoAsync(Accion)).ReturnsAsync(new Accion { Codigo = Accion, Activo = false });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task AccionNoAsignada_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])> { ["CONTROL"] = (true, new[] { "OTRA_ACCION" }) });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task UsuarioConVariosRoles_ObtieneLaUnionDeAcciones()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])>
                {
                    ["ORGANIZADOR"] = (true, new[] { "EVENTO_CREAR" }),
                    ["CONTROL"] = (true, new[] { Accion }),
                });

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(context);

            Assert.True(context.HasSucceeded);
        }

        [Fact]
        public async Task SoloClaimLegacyRoleSinAccionReal_NoAutoriza()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])> { ["CLIENTE"] = (true, new[] { "TICKET_COMPRAR" }) });

            // El claim legacy dice "Control" (el rol que en el modelo viejo habilitaba
            // TICKET_VALIDAR), pero el modelo nuevo solo le dio CLIENTE con TICKET_COMPRAR.
            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid, legacyRoleClaim: "Control"));
            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }

        [Fact]
        public async Task AccionRealAutoriza_AunqueFalteOTengaOtroClaimLegacy()
        {
            var (handler, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            SetupUsuarioConRoles(usuarioRepository, rolRepository, accionRepository, "usuario-1", true,
                new Dictionary<string, (bool, string[])> { ["CONTROL"] = (true, new[] { Accion }) });

            // Sin claim legacy en absoluto.
            var contextSinClaim = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));
            await handler.HandleAsync(contextSinClaim);
            Assert.True(contextSinClaim.HasSucceeded);

            // Con claim legacy de OTRO rol ("Cliente"), la acción real sigue autorizando.
            var contextOtroClaim = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid, legacyRoleClaim: "Cliente"));
            await handler.HandleAsync(contextOtroClaim);
            Assert.True(contextOtroClaim.HasSucceeded);
        }

        [Fact]
        public async Task ErrorInternoDeResolucion_NoAutoriza_NoLanza()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, Uid)).ThrowsAsync(new InvalidOperationException("boom"));
            var permissionService = new PermissionService(usuarioRepository.Object, Mock.Of<IRolRepository>(), Mock.Of<IAccionRepository>());
            var handler = new AccionAuthorizationHandler(permissionService, NullLogger<AccionAuthorizationHandler>.Instance);

            var context = BuildContext(new AccionRequirement(Accion), PrincipalConUid(Uid));

            await handler.HandleAsync(context);

            Assert.False(context.HasSucceeded);
        }
    }
}
