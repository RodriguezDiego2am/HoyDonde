using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class PurchasesControllerTests : IClassFixture<TestApplicationFactory>
    {
        private readonly HttpClient _client;
        public PurchasesControllerTests(TestApplicationFactory factory)
        {
            factory.GrantAccion("test-uid-123", "purchase-user", "purchase-persona", HoyDonde.API.Authorization.Acciones.TicketComprar);
            _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Test");
        }

        [Fact]
        public async Task Start_ResolvesAuthenticatedUidToPersonaBeforeStartingCheckout()
        {
            var checkout = new Mock<IPurchaseCheckoutService>();
            var resolver = new Mock<IAuthenticatedPersonaResolver>();
            resolver.Setup(x => x.ResolvePersonaIdAsync("firebase-uid")).ReturnsAsync("persona-123");
            checkout.Setup(x => x.StartAsync("persona-123", It.IsAny<PurchaseStartRequest>(), "request-key", It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new PurchaseStatusResponse { Id = "purchase-1" });
            var sut = new HoyDonde.API.Controllers.PurchasesController(
                Options.Create(new MercadoPagoOptions { Enabled = true }), checkout.Object, resolver.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
            };
            sut.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "firebase-uid") }, "test"));

            var result = await sut.Start(new PurchaseStartRequest { EventoId = "event-1", TicketTypeId = "type-1", Cantidad = 1 }, "request-key");

            Assert.IsType<ObjectResult>(result);
            resolver.Verify(x => x.ResolvePersonaIdAsync("firebase-uid"), Times.Once);
            checkout.Verify(x => x.StartAsync("persona-123", It.IsAny<PurchaseStartRequest>(), "request-key", It.IsAny<System.Threading.CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Start_WhenProviderRolloutIsDisabled_ReturnsServiceUnavailableWithoutStartingCheckout()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases")
            {
                Content = JsonContent.Create(new PurchaseStartRequest { EventoId = "event-1", TicketTypeId = "type-1", Cantidad = 1 })
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", "purchase-attempt-1");

            var response = await _client.SendAsync(request);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
    }
}
