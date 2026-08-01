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

        private static (UserService sut, Mock<IUserRepository> userRepository, Mock<IEventService> eventService) CreateSut()
        {
            var userRepository = new Mock<IUserRepository>();
            var eventService = new Mock<IEventService>();
            var sut = new UserService(userRepository.Object, eventService.Object);
            return (sut, userRepository, eventService);
        }

        [Fact]
        public async Task RegisterControlAsync_ForOwnEvent_DoesNotRejectOnOwnership()
        {
            var (sut, userRepository, eventService) = CreateSut();
            eventService
                .Setup(s => s.GetByIdAsync(EventId))
                .ReturnsAsync(new Event { Id = EventId, OrganizadorId = OrganizadorId });

            // La creación real en Firebase Auth no se puede ejercitar en este entorno (no hay
            // credenciales/emulador: FirebaseAuth.DefaultInstance lanza porque no hay FirebaseApp
            // inicializada en el proceso de test). Lo que sí podemos probar sin ambigüedad es que
            // el gate de ownership no rechaza al dueño real del evento, es decir que si falla, falla
            // por otra razón distinta a "evento no encontrado" o "evento ajeno".
            var ex = await Record.ExceptionAsync(() =>
                sut.RegisterControlAsync("control1", "Password123!", EventId, OrganizadorId));

            Assert.IsNotType<EventNotFoundException>(ex);
            Assert.IsNotType<EventOwnershipException>(ex);
        }

        [Fact]
        public async Task RegisterControlAsync_ForForeignEvent_ThrowsOwnershipException_AndNeverCreatesUser()
        {
            var (sut, userRepository, eventService) = CreateSut();
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
            var (sut, userRepository, eventService) = CreateSut();
            eventService
                .Setup(s => s.GetByIdAsync(EventId))
                .ReturnsAsync((Event?)null);

            await Assert.ThrowsAsync<EventNotFoundException>(() =>
                sut.RegisterControlAsync("control1", "Password123!", EventId, OrganizadorId));

            userRepository.Verify(r => r.CreateUserAsync(It.IsAny<ApplicationUser>()), Times.Never);
        }
    }
}
