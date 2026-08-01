using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Ejercita UserService.RegisterControlAsync directamente (sin HTTP/TestApplicationFactory),
    // porque UserControllerTests mockea IUserService por completo y por lo tanto no puede probar
    // la comprobación real de propiedad del evento ni que la creación de usuario no se dispare.
    public class UserServiceControlOwnershipTests
    {
        private const string OrganizadorId = "organizador-1";
        private const string EventId = "event-1";

        private static (UserService sut, Mock<IUserRepository> userRepository, Mock<IEventService> eventService, Mock<IIdentityProvider> identityProvider) CreateSut()
        {
            var userRepository = new Mock<IUserRepository>();
            var eventService = new Mock<IEventService>();
            var identityProvider = new Mock<IIdentityProvider>();
            identityProvider
                .Setup(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .ReturnsAsync(new IdentityCreationResult("control-uid-1", FirebaseIdentityProvider.ProviderName));
            var sut = new UserService(userRepository.Object, eventService.Object, identityProvider.Object);
            return (sut, userRepository, eventService, identityProvider);
        }

        [Fact]
        public async Task RegisterControlAsync_ForOwnEvent_DoesNotRejectOnOwnership()
        {
            var (sut, userRepository, eventService, identityProvider) = CreateSut();
            eventService
                .Setup(s => s.GetByIdAsync(EventId))
                .ReturnsAsync(new Event { Id = EventId, OrganizadorId = OrganizadorId });

            // Con IIdentityProvider abstraído (Etapa 0 del refactor de seguridad), la creación de
            // identidad ya se puede mockear en lugar de depender de que FirebaseAuth.DefaultInstance
            // falle por falta de credenciales/emulador. El gate de ownership no debe rechazar al
            // dueño real del evento.
            var ex = await Record.ExceptionAsync(() =>
                sut.RegisterControlAsync("control1", "Password123!", EventId, OrganizadorId));

            Assert.Null(ex);
            userRepository.Verify(r => r.CreateUserAsync(It.Is<ApplicationUser>(u => u.Id == "control-uid-1")), Times.Once);
        }

        [Fact]
        public async Task RegisterControlAsync_ForForeignEvent_ThrowsOwnershipException_AndNeverCreatesUser()
        {
            var (sut, userRepository, eventService, _) = CreateSut();
            eventService
                .Setup(s => s.GetByIdAsync(EventId))
                .ReturnsAsync(new Event { Id = EventId, OrganizadorId = "otro-organizador" });

            await Assert.ThrowsAsync<EventOwnershipException>(() =>
                sut.RegisterControlAsync("control1", "Password123!", EventId, OrganizadorId));

            userRepository.Verify(r => r.CreateUserAsync(It.IsAny<ApplicationUser>()), Times.Never);
        }

        [Fact]
        public async Task RegisterControlAsync_ForNonexistentEvent_ThrowsNotFoundException_AndNeverCreatesUser()
        {
            var (sut, userRepository, eventService, _) = CreateSut();
            eventService
                .Setup(s => s.GetByIdAsync(EventId))
                .ReturnsAsync((Event?)null);

            await Assert.ThrowsAsync<EventNotFoundException>(() =>
                sut.RegisterControlAsync("control1", "Password123!", EventId, OrganizadorId));

            userRepository.Verify(r => r.CreateUserAsync(It.IsAny<ApplicationUser>()), Times.Never);
        }
    }
}
