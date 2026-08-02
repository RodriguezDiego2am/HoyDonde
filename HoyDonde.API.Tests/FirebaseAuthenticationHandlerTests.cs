using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using HoyDonde.API.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Prueba FirebaseAuthenticationHandler de forma aislada: un host mínimo (TestServer) que
    // registra el esquema "Firebase" real contra un IFirebaseIdTokenVerifier mockeado (nunca
    // contra FirebaseAuth.DefaultInstance) y expone un único endpoint [Authorize] simple, sin
    // ninguna policy de Acciones, para poder inspeccionar exactamente qué claims produjo el
    // handler. Las policies de Acciones/IPermissionService en sí (nunca alimentadas por el
    // token) ya están cubiertas por el resto de la suite (EventsControllerTests,
    // SecurityAdminControllerTests, etc. via TestApplicationFactory), que no cambió.
    public class FirebaseAuthenticationHandlerTests : IAsyncLifetime
    {
        private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

        public Mock<IFirebaseIdTokenVerifier> MockVerifier { get; } = new();

        private IHost _host = null!;
        private TestServer _server = null!;

        public async Task InitializeAsync()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.ConfigureServices(services =>
                    {
                        services.AddSingleton(MockVerifier.Object);
                        services.AddAuthentication(FirebaseAuthenticationDefaults.AuthenticationScheme)
                            .AddScheme<AuthenticationSchemeOptions, FirebaseAuthenticationHandler>(
                                FirebaseAuthenticationDefaults.AuthenticationScheme, options => { });
                        services.AddAuthorization();
                        services.AddRouting();
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGet("/protected", (HttpContext ctx) => Results.Ok(new ProtectedResponse(
                                ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                                ctx.User.FindFirst("user_id")?.Value,
                                ctx.User.FindFirst("sub")?.Value,
                                ctx.User.FindFirst(ClaimTypes.Email)?.Value,
                                ctx.User.Claims.Any(c => c.Type == ClaimTypes.Role || c.Type == "role"))))
                                .RequireAuthorization();
                        });
                    });
                });

            _host = await hostBuilder.StartAsync();
            _server = _host.GetTestServer();
        }

        public async Task DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        private record ProtectedResponse(string? Uid, string? UserId, string? Sub, string? Email, bool HasRoleClaim);

        [Fact]
        public async Task ValidToken_AuthenticatesWithUidAndEmailClaims_AndNoRoleClaim()
        {
            MockVerifier
                .Setup(v => v.VerifyIdTokenAsync("valid-token"))
                .ReturnsAsync(new VerifiedFirebaseToken("uid-123", "user@example.com"));

            var client = _server.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token");

            var response = await client.GetAsync("/protected");
            var payload = await response.Content.ReadFromJsonAsync<ProtectedResponse>(CaseInsensitive);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(payload);
            Assert.Equal("uid-123", payload!.Uid);
            Assert.Equal("uid-123", payload.UserId);
            Assert.Equal("uid-123", payload.Sub);
            Assert.Equal("user@example.com", payload.Email);
            Assert.False(payload.HasRoleClaim);
        }

        [Fact]
        public async Task ValidToken_WithoutEmailClaim_OmitsEmail()
        {
            MockVerifier
                .Setup(v => v.VerifyIdTokenAsync("valid-token-no-email"))
                .ReturnsAsync(new VerifiedFirebaseToken("uid-456", null));

            var client = _server.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid-token-no-email");

            var response = await client.GetAsync("/protected");
            var payload = await response.Content.ReadFromJsonAsync<ProtectedResponse>(CaseInsensitive);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("uid-456", payload!.Uid);
            Assert.Null(payload.Email);
        }

        [Fact]
        public async Task InvalidToken_ReturnsUnauthorized()
        {
            MockVerifier
                .Setup(v => v.VerifyIdTokenAsync("bad-token"))
                .ThrowsAsync(new Exception("Token de Firebase rechazado"));

            var client = _server.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "bad-token");

            var response = await client.GetAsync("/protected");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task MissingAuthorizationHeader_ReturnsUnauthorized()
        {
            var client = _server.CreateClient();

            var response = await client.GetAsync("/protected");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            MockVerifier.Verify(v => v.VerifyIdTokenAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task NonBearerAuthorizationHeader_ReturnsUnauthorized()
        {
            var client = _server.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", "dXNlcjpwYXNz");

            var response = await client.GetAsync("/protected");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            MockVerifier.Verify(v => v.VerifyIdTokenAsync(It.IsAny<string>()), Times.Never);
        }
    }
}
