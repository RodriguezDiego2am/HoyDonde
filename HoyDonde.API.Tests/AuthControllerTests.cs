using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class AuthControllerTests : IClassFixture<TestApplicationFactory>
    {
        private const string TokenUid = "test-uid-123";
        private const string TokenEmail = "test@example.com";

        private readonly TestApplicationFactory _factory;
        private readonly System.Net.Http.HttpClient _client;

        public AuthControllerTests(TestApplicationFactory factory)
        {
            _factory = factory;
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        }

        [Fact]
        public async Task SyncUser_WithoutToken_ReturnsUnauthorized()
        {
            var anonClient = _factory.CreateClient(); // sin Authorization header

            var response = await anonClient.PostAsJsonAsync("/api/auth/sync", new SyncClienteRequestDto());

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task SyncUser_UsesUidAndEmailFromToken_NeverFromBody()
        {
            // El DTO de body no tiene ningún campo de uid/email posible: la única forma de que
            // el servicio reciba "test-uid-123"/"test@example.com" es que el controller los haya
            // tomado del token (FakeAuthHandler), nunca de lo que viaja en el body.
            _factory.MockAuthService
                .Setup(s => s.SyncClienteAsync(TokenUid, TokenEmail, It.IsAny<SyncClienteRequest>()))
                .ReturnsAsync(new SyncClienteResult("usuario-1", "persona-1", new List<string> { "CLIENTE" }));

            var body = new SyncClienteRequestDto { FullName = "Juan Perez", Dni = "12345678", PhoneNumber = "+5491122334455" };

            var response = await _client.PostAsJsonAsync("/api/auth/sync", body);

            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new System.Exception(content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            _factory.MockAuthService.Verify(s => s.SyncClienteAsync(TokenUid, TokenEmail,
                It.Is<SyncClienteRequest>(r => r.FullName == "Juan Perez" && r.Dni == "12345678" && r.PhoneNumber == "+5491122334455")), Times.Once);
        }

        [Fact]
        public async Task SyncUser_ReturnsRolesFromService()
        {
            _factory.MockAuthService
                .Setup(s => s.SyncClienteAsync(TokenUid, TokenEmail, It.IsAny<SyncClienteRequest>()))
                .ReturnsAsync(new SyncClienteResult("usuario-2", "persona-2", new List<string> { "CLIENTE" }));

            var response = await _client.PostAsJsonAsync("/api/auth/sync", new SyncClienteRequestDto());
            var payload = await response.Content.ReadFromJsonAsync<SyncUserResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(payload);
            Assert.Equal("usuario-2", payload!.UsuarioId);
            Assert.Equal("persona-2", payload.PersonaId);
            Assert.Contains("CLIENTE", payload.Roles);
        }

        [Fact]
        public async Task SyncUser_WhenActorHasOtherRoles_ReturnsThoseRoles()
        {
            _factory.MockAuthService
                .Setup(s => s.SyncClienteAsync(TokenUid, TokenEmail, It.IsAny<SyncClienteRequest>()))
                .ReturnsAsync(new SyncClienteResult("usuario-3", "persona-3", new List<string> { "ADMINISTRADOR" }));

            var response = await _client.PostAsJsonAsync("/api/auth/sync", new SyncClienteRequestDto());
            var payload = await response.Content.ReadFromJsonAsync<SyncUserResponseDto>();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain("CLIENTE", payload!.Roles);
            Assert.Contains("ADMINISTRADOR", payload.Roles);
        }
    }
}
