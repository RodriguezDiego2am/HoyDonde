using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class UserControllerTests : IClassFixture<TestApplicationFactory>
    {
        private const string ActorUid = "test-uid-123";

        private readonly TestApplicationFactory _factory;
        private readonly System.Net.Http.HttpClient _client;

        public UserControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            // Habilitamos el Fake Auth que creamos en el TestApplicationFactory
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        }

        [Fact]
        public async Task RegisterControl_WithoutOrganizerRole_ReturnsUnauthorized()
        {
            // Arrange
            var request = new RegisterControlDto
            {
                UserName = "control_puerta",
                Password = "Password123!",
                EventId = "event-123"
            };

            // Para simular un usuario SIN rol de Organizador, creamos un cliente sin el header
            var unauthClient = _factory.CreateClient();
            // no auth header set

            // Act
            var response = await unauthClient.PostAsJsonAsync("/api/users/control", request);

            // Assert - Should be 401 Unauthorized
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task RegisterControl_WithOrganizerRole_ReturnsOk()
        {
            // Arrange
            var request = new RegisterControlDto
            {
                UserName = "control_vip",
                Password = "Password123!",
                EventId = "event-vip"
            };

            // Setup the mock to accept the creation
            _factory.MockUserService
                .Setup(s => s.RegisterControlAsync(ActorUid, request.UserName, request.Password, request.EventId))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-1", "usuario-1"));

            // Act - Usamos el client por defecto que TIENE el rol de Organizador emulado
            var response = await _client.PostAsJsonAsync("/api/users/control", request);

            // Assert
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RegisterControl_ForForeignEvent_ReturnsForbidden()
        {
            var request = new RegisterControlDto
            {
                UserName = "control_ajeno",
                Password = "Password123!",
                EventId = "event-ajeno"
            };

            _factory.MockUserService
                .Setup(s => s.RegisterControlAsync(ActorUid, request.UserName, request.Password, request.EventId))
                .ThrowsAsync(new EventOwnershipException(request.EventId, ActorUid));

            var response = await _client.PostAsJsonAsync("/api/users/control", request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task RegisterControl_ForNonexistentEvent_ReturnsNotFound()
        {
            var request = new RegisterControlDto
            {
                UserName = "control_inexistente",
                Password = "Password123!",
                EventId = "event-inexistente"
            };

            _factory.MockUserService
                .Setup(s => s.RegisterControlAsync(ActorUid, request.UserName, request.Password, request.EventId))
                .ThrowsAsync(new EventNotFoundException(request.EventId));

            var response = await _client.PostAsJsonAsync("/api/users/control", request);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task RegisterControl_WithNonOrganizerRole_ReturnsForbidden()
        {
            var request = new RegisterControlDto
            {
                UserName = "control_x",
                Password = "Password123!",
                EventId = "event-1"
            };

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/control")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", Models.Roles.Cliente);

            var response = await _client.SendAsync(reqMessage);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task RegisterControl_WhenEmailAlreadyExists_ReturnsConflict()
        {
            var request = new RegisterControlDto
            {
                UserName = "control_duplicado",
                Password = "Password123!",
                EventId = "event-1"
            };

            _factory.MockUserService
                .Setup(s => s.RegisterControlAsync(ActorUid, request.UserName, request.Password, request.EventId))
                .ThrowsAsync(new IdentityEmailAlreadyExistsException($"{request.UserName}@control.hoydonde.com"));

            var response = await _client.PostAsJsonAsync("/api/users/control", request);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // ---- RegisterAdmin: solo un Admin autenticado puede crear otro admin ----

        [Fact]
        public async Task RegisterAdmin_Anonymous_ReturnsUnauthorized()
        {
            var request = new RegisterAdminDto { Email = "nuevo-admin@test.com", Password = "Password123!" };

            var anonClient = _factory.CreateClient(); // sin Authorization header

            var response = await anonClient.PostAsJsonAsync("/api/users/admin", request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Cliente")]
        [InlineData("Organizador")]
        public async Task RegisterAdmin_WithNonAdminRole_ReturnsForbidden(string role)
        {
            var request = new RegisterAdminDto { Email = "nuevo-admin@test.com", Password = "Password123!" };

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/admin")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", role);

            var response = await _client.SendAsync(reqMessage);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task RegisterAdmin_WithAdminRole_ReturnsOk()
        {
            var request = new RegisterAdminDto { Email = "nuevo-admin@test.com", Password = "Password123!" };

            _factory.MockUserService
                .Setup(s => s.RegisterAdminAsync(ActorUid, request.Email, request.Password))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-2", "usuario-2"));

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/admin")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", Models.Roles.Admin);

            var response = await _client.SendAsync(reqMessage);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task RegisterAdmin_WhenEmailAlreadyExists_ReturnsConflict()
        {
            var request = new RegisterAdminDto { Email = "duplicado@test.com", Password = "Password123!" };

            _factory.MockUserService
                .Setup(s => s.RegisterAdminAsync(ActorUid, request.Email, request.Password))
                .ThrowsAsync(new IdentityEmailAlreadyExistsException(request.Email));

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/admin")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", Models.Roles.Admin);

            var response = await _client.SendAsync(reqMessage);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }

        // ---- RegisterOrganizador: los organizadores no pueden autorregistrarse ----

        [Fact]
        public async Task RegisterOrganizador_Anonymous_ReturnsUnauthorized()
        {
            var request = new RegisterOrganizadorDto { Email = "nuevo-organizador@test.com", Password = "Password123!" };

            var anonClient = _factory.CreateClient();

            var response = await anonClient.PostAsJsonAsync("/api/users/organizador", request);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Theory]
        [InlineData("Cliente")]
        [InlineData("Organizador")]
        [InlineData("Control")]
        public async Task RegisterOrganizador_WithNonAdminRole_ReturnsForbidden(string role)
        {
            var request = new RegisterOrganizadorDto { Email = "nuevo-organizador@test.com", Password = "Password123!" };

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/organizador")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", role);

            var response = await _client.SendAsync(reqMessage);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task RegisterOrganizador_WithAdminRole_ReturnsOk()
        {
            var request = new RegisterOrganizadorDto { Email = "nuevo-organizador@test.com", Password = "Password123!" };

            _factory.MockUserService
                .Setup(s => s.RegisterOrganizadorAsync(ActorUid, request.Email, request.Password))
                .ReturnsAsync(new UsuarioProvisioningResult("persona-3", "usuario-3"));

            var reqMessage = new HttpRequestMessage(HttpMethod.Post, "/api/users/organizador")
            {
                Content = JsonContent.Create(request)
            };
            reqMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
            reqMessage.Headers.Add("Test-Role", Models.Roles.Admin);

            var response = await _client.SendAsync(reqMessage);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
