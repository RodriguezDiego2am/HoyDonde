using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Commands;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Bootstrap del primer Administrador contra Firestore Emulator real
    // (docs/security-refactor-plan.md §5, Etapa 3, criterio E): ejercita la collection group
    // query real de GetUsuarioIdsConRolActivoAsync, que no tiene cobertura útil con mocks. El
    // proveedor de identidad se mockea (no se ejercita Firebase Auth acá). Un único test method
    // encadena "primer bootstrap exitoso" -> "segundo bootstrap rechazado" para no depender del
    // orden de ejecución entre tests ni de estado dejado por otras clases en el mismo emulador.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class BootstrapAdminCommandEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public BootstrapAdminCommandEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private static IConfiguration EnabledConfiguration()
        {
            return new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:AllowAdminBootstrap"] = "true" })
                .Build();
        }

        private BootstrapAdminCommand CreateSut(Mock<IIdentityProvider> identityProvider, IUsuarioRepository usuarioRepository, IRolRepository rolRepository, IAccionRepository accionRepository)
        {
            var identidadHuerfanaRepository = new FirestoreIdentidadHuerfanaRepository(_fixture.Db!);
            var eventService = new Mock<IEventService>(); // no lo usa ningún camino de Admin
            var personaResolver = new Mock<IAuthenticatedPersonaResolver>(); // no lo usa RegisterAdminAsync
            var controlAsignacionRepository = new Mock<IControlAsignacionRepository>(); // no lo usa RegisterAdminAsync
            var userService = new UserService(
                usuarioRepository, identidadHuerfanaRepository, eventService.Object, identityProvider.Object,
                personaResolver.Object, controlAsignacionRepository.Object, NullLogger<UserService>.Instance);
            var seeder = new SecurityCatalogSeeder(rolRepository, accionRepository);

            return new BootstrapAdminCommand(
                EnabledConfiguration(), seeder, usuarioRepository, rolRepository, userService,
                NullLogger<BootstrapAdminCommand>.Instance);
        }

        [FirestoreEmulatorFact]
        public async Task RunAsync_SecondAdminAttempt_IsRejected_AfterFirstSucceeds()
        {
            var usuarioRepository = new FirestoreUsuarioRepository(_fixture.Db!);
            var rolRepository = new FirestoreRolRepository(_fixture.Db!);
            var accionRepository = new FirestoreAccionRepository(_fixture.Db!);

            var identityProvider = new Mock<IIdentityProvider>();
            var uidCounter = 0;
            identityProvider
                .Setup(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(() => Task.FromResult(new IdentityCreationResult($"uid-bootstrap-{Guid.NewGuid():N}-{uidCounter++}", FirebaseIdentityProvider.ProviderName)));

            var sut = CreateSut(identityProvider, usuarioRepository, rolRepository, accionRepository);

            var primerEmail = $"admin-boot-{Guid.NewGuid():N}@test.com";
            var primerExitCode = await sut.RunAsync(new[] { "bootstrap-admin", primerEmail }, passwordReader: () => "Password123!");
            Assert.Equal(0, primerExitCode);

            var segundoEmail = $"admin-boot-2-{Guid.NewGuid():N}@test.com";
            var segundoExitCode = await sut.RunAsync(new[] { "bootstrap-admin", segundoEmail }, passwordReader: () => "Password123!");
            Assert.NotEqual(0, segundoExitCode);

            // Solo el primer bootstrap creó una identidad: el segundo se abortó antes de llamar
            // a IIdentityProvider.
            identityProvider.Verify(p => p.CreateIdentityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        }
    }
}
