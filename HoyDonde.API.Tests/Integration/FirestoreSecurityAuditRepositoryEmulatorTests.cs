using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // ISecurityAuditRepository.GetByRangoAsync (docs/api-mvp-plan.md §11.3/§11.4) contra Firestore
    // Emulator real: rango Desde-inclusivo/Hasta-exclusivo sobre Timestamp y orden descendente. El
    // filtrado en memoria (Operacion/ActorUsuarioId/TargetTipo/TargetId) se cubre en
    // SecurityAuditReportServiceTests, sin Firestore.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreSecurityAuditRepositoryEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public FirestoreSecurityAuditRepositoryEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task SeedAuditAsync(DateTime timestamp, string operacion = "ROL_CREAR", string targetTipo = "Rol", string targetId = "ROL_TEST")
        {
            var audit = new SecurityAudit
            {
                Id = Guid.NewGuid().ToString(),
                ActorUsuarioId = $"actor-{Guid.NewGuid():N}",
                ActorPersonaId = $"persona-{Guid.NewGuid():N}",
                Operacion = operacion,
                TargetTipo = targetTipo,
                TargetId = targetId,
                Detalle = string.Empty,
                Timestamp = timestamp,
            };
            await _fixture.Db!.Collection("security_audits").Document(audit.Id).SetAsync(audit);
        }

        [FirestoreEmulatorFact]
        public async Task GetByRangoAsync_DesdeInclusive_HastaExclusive()
        {
            var baseFecha = DateTime.UtcNow.AddDays(700);
            var sut = new FirestoreSecurityAuditRepository(_fixture.Db!);

            var desde = baseFecha;
            var hasta = baseFecha.AddDays(3);

            await SeedAuditAsync(desde.AddSeconds(-2), targetId: "antes-de-desde");
            await SeedAuditAsync(desde.AddSeconds(1), targetId: "justo-despues-de-desde");
            await SeedAuditAsync(hasta.AddSeconds(-1), targetId: "justo-antes-de-hasta");
            await SeedAuditAsync(hasta.AddSeconds(1), targetId: "justo-despues-de-hasta");

            var resultado = await sut.GetByRangoAsync(desde, hasta);
            var targetIds = resultado.Select(a => a.TargetId).ToList();

            Assert.DoesNotContain("antes-de-desde", targetIds);
            Assert.Contains("justo-despues-de-desde", targetIds);
            Assert.Contains("justo-antes-de-hasta", targetIds);
            Assert.DoesNotContain("justo-despues-de-hasta", targetIds);
        }

        [FirestoreEmulatorFact]
        public async Task GetByRangoAsync_OrdenaDescendentePorTimestamp()
        {
            var baseFecha = DateTime.UtcNow.AddDays(710);
            var sut = new FirestoreSecurityAuditRepository(_fixture.Db!);

            await SeedAuditAsync(baseFecha, targetId: "primero");
            await SeedAuditAsync(baseFecha.AddHours(1), targetId: "segundo");
            await SeedAuditAsync(baseFecha.AddHours(2), targetId: "tercero");

            var resultado = await sut.GetByRangoAsync(baseFecha.AddMinutes(-1), baseFecha.AddHours(3));
            var targetIds = resultado.Select(a => a.TargetId).ToList();

            Assert.Equal(new[] { "tercero", "segundo", "primero" }, targetIds);
        }

        [FirestoreEmulatorFact]
        public async Task GetByRangoAsync_SinAuditoriasEnRango_ReturnsEmpty()
        {
            var baseFecha = DateTime.UtcNow.AddDays(720);
            var sut = new FirestoreSecurityAuditRepository(_fixture.Db!);

            var resultado = await sut.GetByRangoAsync(baseFecha, baseFecha.AddDays(1));

            Assert.Empty(resultado);
        }
    }
}
