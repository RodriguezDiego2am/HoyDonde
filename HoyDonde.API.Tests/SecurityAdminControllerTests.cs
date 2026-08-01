using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Contratos HTTP de administración de seguridad (docs/security-refactor-plan.md §6, Etapa 5).
    // ISecurityAdminService queda mockeado (mismo patrón que MockEventService): estos tests
    // cubren ruteo, mapeo de policy por endpoint y contratos HTTP (200/400/403/404/409), no la
    // lógica de negocio del servicio (esa se cubre en tests de integración contra el emulador).
    public class SecurityAdminControllerTests : IClassFixture<TestApplicationFactory>
    {
        private const string ActorUid = "test-uid-123";

        private readonly TestApplicationFactory _factory;
        private readonly HttpClient _client;

        public SecurityAdminControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

            _factory.GrantAccion(ActorUid, "usuario-security-admin-test", "persona-security-admin-test",
                Acciones.RolCrear, Acciones.RolEditar, Acciones.RolActivar, Acciones.RolAsignarAccion, Acciones.RolQuitarAccion,
                Acciones.UsuarioAsignarRol, Acciones.UsuarioQuitarRol, Acciones.UsuarioVerPermisosEfectivos, Acciones.UsuarioDesactivar);
        }

        private static HttpRequestMessage SinAccion(HttpMethod method, string path)
        {
            var msg = new HttpRequestMessage(method, path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", "uid-sin-permiso-security-admin");
            return msg;
        }

        // ---- Roles ----

        [Fact]
        public async Task CrearRol_ConAccionRolCrear_ReturnsOk()
        {
            var request = new CreateRolRequestDto { Codigo = "SOPORTE", Nombre = "Soporte", Descripcion = "Atiende incidentes." };
            _factory.MockSecurityAdminService
                .Setup(s => s.CrearRolAsync(ActorUid, It.IsAny<CreateRolRequestDto>()))
                .ReturnsAsync(new RolResponseDto { Codigo = "SOPORTE", Nombre = "Soporte", Activo = true });

            var response = await _client.PostAsJsonAsync("/api/security/roles", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task CrearRol_SinAccionRolCrear_ReturnsForbidden()
        {
            var request = new CreateRolRequestDto { Codigo = "SOPORTE", Nombre = "Soporte", Descripcion = "x" };
            var msg = SinAccion(HttpMethod.Post, "/api/security/roles");
            msg.Content = JsonContent.Create(request);

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task CrearRol_CodigoDuplicado_ReturnsConflict()
        {
            var request = new CreateRolRequestDto { Codigo = "ADMINISTRADOR", Nombre = "x", Descripcion = "x" };
            _factory.MockSecurityAdminService
                .Setup(s => s.CrearRolAsync(ActorUid, It.IsAny<CreateRolRequestDto>()))
                .ThrowsAsync(new RolYaExisteException("ADMINISTRADOR"));

            var response = await _client.PostAsJsonAsync("/api/security/roles", request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task CrearRol_CodigoInvalido_ReturnsBadRequest()
        {
            var request = new CreateRolRequestDto { Codigo = "invalido/con-barra", Nombre = "x", Descripcion = "x" };
            _factory.MockSecurityAdminService
                .Setup(s => s.CrearRolAsync(ActorUid, It.IsAny<CreateRolRequestDto>()))
                .ThrowsAsync(new System.ArgumentException("código inválido"));

            var response = await _client.PostAsJsonAsync("/api/security/roles", request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task EditarRol_ConAccionRolEditar_ReturnsOk()
        {
            var request = new UpdateRolRequestDto { Nombre = "Nuevo nombre", Descripcion = "x" };
            _factory.MockSecurityAdminService
                .Setup(s => s.EditarRolAsync(ActorUid, "ORGANIZADOR", It.IsAny<UpdateRolRequestDto>()))
                .ReturnsAsync(new RolResponseDto { Codigo = "ORGANIZADOR", Nombre = "Nuevo nombre", Activo = true });

            var response = await _client.PutAsJsonAsync("/api/security/roles/ORGANIZADOR", request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task EditarRol_SinAccionRolEditar_ReturnsForbidden()
        {
            var request = new UpdateRolRequestDto { Nombre = "x", Descripcion = "x" };
            var msg = SinAccion(HttpMethod.Put, "/api/security/roles/ORGANIZADOR");
            msg.Content = JsonContent.Create(request);

            var response = await _client.SendAsync(msg);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task EditarRol_Inexistente_ReturnsNotFound()
        {
            var request = new UpdateRolRequestDto { Nombre = "x", Descripcion = "x" };
            _factory.MockSecurityAdminService
                .Setup(s => s.EditarRolAsync(ActorUid, "NO_EXISTE", It.IsAny<UpdateRolRequestDto>()))
                .ThrowsAsync(new RolNoEncontradoException("NO_EXISTE"));

            var response = await _client.PutAsJsonAsync("/api/security/roles/NO_EXISTE", request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ActivarRol_ConAccionRolActivar_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.SetRolActivoAsync(ActorUid, "CONTROL", true)).Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/security/roles/CONTROL/activar", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarRol_SinAccionRolActivar_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Post, "/api/security/roles/CONTROL/desactivar"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarRol_DejariaCeroAdministradoresEfectivos_ReturnsConflict()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.SetRolActivoAsync(ActorUid, "ADMINISTRADOR", false))
                .ThrowsAsync(new UltimoAdministradorException());

            var response = await _client.PostAsync("/api/security/roles/ADMINISTRADOR/desactivar", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarRol_Inexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.SetRolActivoAsync(ActorUid, "NO_EXISTE", false))
                .ThrowsAsync(new RolNoEncontradoException("NO_EXISTE"));

            var response = await _client.PostAsync("/api/security/roles/NO_EXISTE/desactivar", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task ListarRoles_ConAccionRolEditar_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ListarRolesAsync())
                .ReturnsAsync(new System.Collections.Generic.List<RolResponseDto>());

            var response = await _client.GetAsync("/api/security/roles");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ListarRoles_SinAccionRolEditar_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Get, "/api/security/roles"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task ObtenerRol_Inexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ObtenerRolAsync("NO_EXISTE"))
                .ThrowsAsync(new RolNoEncontradoException("NO_EXISTE"));

            var response = await _client.GetAsync("/api/security/roles/NO_EXISTE");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        // ---- Catálogo de Acciones ----

        [Fact]
        public async Task ListarAcciones_ConAccionRolAsignarAccion_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ListarAccionesAsync())
                .ReturnsAsync(new System.Collections.Generic.List<AccionResponseDto>
                {
                    new() { Codigo = "TICKET_VALIDAR", Descripcion = "x", Activo = true },
                });

            var response = await _client.GetAsync("/api/security/acciones");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ListarAcciones_SinAccionRolAsignarAccion_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Get, "/api/security/acciones"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- Acciones de un Rol ----

        [Fact]
        public async Task AsignarAccion_ConAccionRolAsignarAccion_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.AsignarAccionARolAsync(ActorUid, "CONTROL", "TICKET_VALIDAR")).Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/security/roles/CONTROL/acciones/TICKET_VALIDAR", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AsignarAccion_SinAccionRolAsignarAccion_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Post, "/api/security/roles/CONTROL/acciones/TICKET_VALIDAR"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AsignarAccion_AccionInexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.AsignarAccionARolAsync(ActorUid, "CONTROL", "NO_EXISTE"))
                .ThrowsAsync(new AccionNoEncontradaException("NO_EXISTE"));

            var response = await _client.PostAsync("/api/security/roles/CONTROL/acciones/NO_EXISTE", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task QuitarAccion_ConAccionRolQuitarAccion_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.QuitarAccionDeRolAsync(ActorUid, "CONTROL", "TICKET_VALIDAR")).Returns(Task.CompletedTask);

            var response = await _client.DeleteAsync("/api/security/roles/CONTROL/acciones/TICKET_VALIDAR");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task QuitarAccion_SinAccionRolQuitarAccion_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Delete, "/api/security/roles/CONTROL/acciones/TICKET_VALIDAR"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        // ---- Roles de Usuario ----

        [Fact]
        public async Task AsignarRolUsuario_ConAccionUsuarioAsignarRol_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.AsignarRolAUsuarioAsync(ActorUid, "usuario-1", "CONTROL")).Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/security/usuarios/usuario-1/roles/CONTROL", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task AsignarRolUsuario_SinAccionUsuarioAsignarRol_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Post, "/api/security/usuarios/usuario-1/roles/CONTROL"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task AsignarRolUsuario_UsuarioInexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.AsignarRolAUsuarioAsync(ActorUid, "no-existe", "CONTROL"))
                .ThrowsAsync(new UsuarioNoEncontradoException("no-existe"));

            var response = await _client.PostAsync("/api/security/usuarios/no-existe/roles/CONTROL", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task QuitarRolUsuario_ConAccionUsuarioQuitarRol_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.QuitarRolDeUsuarioAsync(ActorUid, "usuario-1", "CONTROL")).Returns(Task.CompletedTask);

            var response = await _client.DeleteAsync("/api/security/usuarios/usuario-1/roles/CONTROL");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task QuitarRolUsuario_SinAccionUsuarioQuitarRol_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Delete, "/api/security/usuarios/usuario-1/roles/CONTROL"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task QuitarRolUsuario_DejariaCeroAdministradoresEfectivos_ReturnsConflict()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.QuitarRolDeUsuarioAsync(ActorUid, "usuario-admin-unico", "ADMINISTRADOR"))
                .ThrowsAsync(new UltimoAdministradorException());

            var response = await _client.DeleteAsync("/api/security/usuarios/usuario-admin-unico/roles/ADMINISTRADOR");

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // ---- Usuario ----

        [Fact]
        public async Task ListarUsuarios_ConAccionUsuarioVerPermisosEfectivos_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ListarUsuariosAsync())
                .ReturnsAsync(new System.Collections.Generic.List<UsuarioResumenResponseDto>
                {
                    new() { UsuarioId = "usuario-1", PersonaId = "persona-1", Email = "a@test.com", Activo = true },
                });

            var response = await _client.GetAsync("/api/security/usuarios");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task ListarUsuarios_SinAccionUsuarioVerPermisosEfectivos_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Get, "/api/security/usuarios"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task PermisosEfectivos_UsuarioInexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ConsultarPermisosEfectivosAsync("no-existe"))
                .ThrowsAsync(new UsuarioNoEncontradoException("no-existe"));

            var response = await _client.GetAsync("/api/security/usuarios/no-existe/permisos-efectivos");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task PermisosEfectivos_ConAccionUsuarioVerPermisosEfectivos_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.ConsultarPermisosEfectivosAsync("usuario-1"))
                .ReturnsAsync(new PermisosEfectivosResponseDto { UsuarioId = "usuario-1", UsuarioActivo = true });

            var response = await _client.GetAsync("/api/security/usuarios/usuario-1/permisos-efectivos");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task PermisosEfectivos_SinAccionUsuarioVerPermisosEfectivos_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Get, "/api/security/usuarios/usuario-1/permisos-efectivos"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarUsuario_ConAccionUsuarioDesactivar_ReturnsOk()
        {
            _factory.MockSecurityAdminService.Setup(s => s.SetUsuarioActivoAsync(ActorUid, "usuario-1", false)).Returns(Task.CompletedTask);

            var response = await _client.PostAsync("/api/security/usuarios/usuario-1/desactivar", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarUsuario_SinAccionUsuarioDesactivar_ReturnsForbidden()
        {
            var response = await _client.SendAsync(SinAccion(HttpMethod.Post, "/api/security/usuarios/usuario-1/desactivar"));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarUsuario_DejariaCeroAdministradoresEfectivos_ReturnsConflict()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.SetUsuarioActivoAsync(ActorUid, "usuario-admin-unico", false))
                .ThrowsAsync(new UltimoAdministradorException());

            var response = await _client.PostAsync("/api/security/usuarios/usuario-admin-unico/desactivar", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        [Fact]
        public async Task DesactivarUsuario_Inexistente_ReturnsNotFound()
        {
            _factory.MockSecurityAdminService
                .Setup(s => s.SetUsuarioActivoAsync(ActorUid, "no-existe", false))
                .ThrowsAsync(new UsuarioNoEncontradoException("no-existe"));

            var response = await _client.PostAsync("/api/security/usuarios/no-existe/desactivar", null);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
