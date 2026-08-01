using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Etapa 2 del refactor de seguridad (docs/security-refactor-plan.md §4/§8, capa 1):
    // la resolución de permisos se prueba con repositorios mockeados. Contra el catálogo real
    // de la Etapa 1 se prueba en HoyDonde.API.Tests/Integration (Firestore Emulator).
    public class PermissionServiceTests
    {
        private const string Provider = "FIREBASE";
        private const string ExternalSubjectId = "uid-1";

        private static (PermissionService sut, Mock<IUsuarioRepository> usuarioRepository, Mock<IRolRepository> rolRepository, Mock<IAccionRepository> accionRepository) CreateSut()
        {
            var usuarioRepository = new Mock<IUsuarioRepository>();
            var rolRepository = new Mock<IRolRepository>();
            var accionRepository = new Mock<IAccionRepository>();
            var sut = new PermissionService(usuarioRepository.Object, rolRepository.Object, accionRepository.Object);
            return (sut, usuarioRepository, rolRepository, accionRepository);
        }

        [Fact]
        public async Task GetPermisosEfectivosAsync_ReturnsEmpty_WhenIdentidadExternaNoExiste()
        {
            var (sut, usuarioRepository, _, _) = CreateSut();
            usuarioRepository
                .Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, ExternalSubjectId))
                .ReturnsAsync((string?)null);

            var resultado = await sut.GetPermisosEfectivosAsync(Provider, ExternalSubjectId);

            Assert.False(resultado.UsuarioActivo);
            Assert.Null(resultado.UsuarioId);
            Assert.Null(resultado.PersonaId);
            Assert.Empty(resultado.Roles);
            Assert.Empty(resultado.Acciones);
        }

        [Fact]
        public async Task TieneAccionAsync_ReturnsFalse_WhenUsuarioInactivo()
        {
            var (sut, usuarioRepository, rolRepository, _) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, ExternalSubjectId)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = false });

            var tiene = await sut.TieneAccionAsync(Provider, ExternalSubjectId, "TICKET_VALIDAR");

            Assert.False(tiene);
            rolRepository.Verify(r => r.GetByCodigoAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task TieneAccionAsync_IgnoresRolInactivo()
        {
            var (sut, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, ExternalSubjectId)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = true });
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync("usuario-1")).ReturnsAsync(new List<string> { "CONTROL" });
            rolRepository.Setup(r => r.GetByCodigoAsync("CONTROL")).ReturnsAsync(new Rol { Codigo = "CONTROL", Activo = false });

            var tiene = await sut.TieneAccionAsync(Provider, ExternalSubjectId, "TICKET_VALIDAR");

            Assert.False(tiene);
            accionRepository.Verify(r => r.GetByCodigoAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task TieneAccionAsync_IgnoresAccionInactiva()
        {
            var (sut, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, ExternalSubjectId)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = true });
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync("usuario-1")).ReturnsAsync(new List<string> { "CONTROL" });
            rolRepository.Setup(r => r.GetByCodigoAsync("CONTROL")).ReturnsAsync(new Rol { Codigo = "CONTROL", Activo = true });
            rolRepository.Setup(r => r.GetAccionCodigosAsync("CONTROL")).ReturnsAsync(new List<string> { "TICKET_VALIDAR" });
            accionRepository.Setup(r => r.GetByCodigoAsync("TICKET_VALIDAR")).ReturnsAsync(new Accion { Codigo = "TICKET_VALIDAR", Activo = false });

            var tiene = await sut.TieneAccionAsync(Provider, ExternalSubjectId, "TICKET_VALIDAR");

            Assert.False(tiene);
        }

        [Fact]
        public async Task TieneAccionAsync_ReturnsTrue_WhenRolYAccionActivos()
        {
            var (sut, usuarioRepository, rolRepository, accionRepository) = CreateSut();
            usuarioRepository.Setup(r => r.GetUsuarioIdByExternalSubjectAsync(Provider, ExternalSubjectId)).ReturnsAsync("usuario-1");
            usuarioRepository.Setup(r => r.GetByIdAsync("usuario-1")).ReturnsAsync(new Usuario { Id = "usuario-1", PersonaId = "persona-1", IsActive = true });
            usuarioRepository.Setup(r => r.GetRolCodigosActivosAsync("usuario-1")).ReturnsAsync(new List<string> { "CONTROL" });
            rolRepository.Setup(r => r.GetByCodigoAsync("CONTROL")).ReturnsAsync(new Rol { Codigo = "CONTROL", Activo = true });
            rolRepository.Setup(r => r.GetAccionCodigosAsync("CONTROL")).ReturnsAsync(new List<string> { "TICKET_VALIDAR" });
            accionRepository.Setup(r => r.GetByCodigoAsync("TICKET_VALIDAR")).ReturnsAsync(new Accion { Codigo = "TICKET_VALIDAR", Activo = true });

            var tiene = await sut.TieneAccionAsync(Provider, ExternalSubjectId, "TICKET_VALIDAR");
            var permisos = await sut.GetPermisosEfectivosAsync(Provider, ExternalSubjectId);

            Assert.True(tiene);
            Assert.Equal("persona-1", permisos.PersonaId);
            Assert.Contains("CONTROL", permisos.Roles);
            Assert.Contains("TICKET_VALIDAR", permisos.Acciones);
        }
    }
}
