using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // GET /api/reports/admin/security-audits (docs/api-mvp-plan.md §11.3): filtros en memoria
    // (Operacion/ActorUsuarioId/TargetTipo/TargetId), orden descendente, resolución batch de
    // ActorEmail y el default de 30 días -todo con ISecurityAuditRepository/IUsuarioRepository
    // mockeados, sin Firestore Emulator (ver Integration/FirestoreSecurityAuditRepositoryEmulatorTests
    // para la query real).
    public class SecurityAuditReportServiceTests
    {
        private static readonly DateTime UtcNow = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        private static SecurityAudit BuildAudit(string operacion, string actorUsuarioId, string targetTipo, string targetId, DateTime timestamp, string detalle = "") => new()
        {
            Id = Guid.NewGuid().ToString(),
            Operacion = operacion,
            ActorUsuarioId = actorUsuarioId,
            ActorPersonaId = $"persona-de-{actorUsuarioId}",
            TargetTipo = targetTipo,
            TargetId = targetId,
            Detalle = detalle,
            Timestamp = timestamp,
        };

        private static (SecurityAuditReportService Sut, Mock<ISecurityAuditRepository> AuditRepo, Mock<IUsuarioRepository> UsuarioRepo) CreateSut(IReadOnlyList<SecurityAudit> audits, IReadOnlyList<Usuario>? usuarios = null)
        {
            var auditRepo = new Mock<ISecurityAuditRepository>();
            auditRepo.Setup(r => r.GetByRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync(audits);

            var usuarioRepo = new Mock<IUsuarioRepository>();
            usuarioRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(usuarios ?? new List<Usuario>());

            return (new SecurityAuditReportService(auditRepo.Object, usuarioRepo.Object), auditRepo, usuarioRepo);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_SinFiltrosOpcionales_DevuelveTodoElRangoOrdenadoDescendente()
        {
            var mas_reciente = BuildAudit("ROL_CREAR", "actor-1", "Rol", "ORGANIZADOR", UtcNow);
            var mas_viejo = BuildAudit("ROL_EDITAR", "actor-1", "Rol", "ORGANIZADOR", UtcNow.AddDays(-1));
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { mas_viejo, mas_reciente });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto { FechaDesde = UtcNow.AddDays(-2), FechaHasta = UtcNow.AddDays(1) });

            Assert.Equal(2, resultado.Auditorias.Count);
            Assert.Equal(mas_reciente.Timestamp, resultado.Auditorias[0].Timestamp);
            Assert.Equal(mas_viejo.Timestamp, resultado.Auditorias[1].Timestamp);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_FiltraPorOperacion()
        {
            var asignar = BuildAudit("ROL_ASIGNAR_ACCION", "actor-1", "RolAccion", "ORGANIZADOR/EVENTO_CREAR", UtcNow);
            var quitar = BuildAudit("ROL_QUITAR_ACCION", "actor-1", "RolAccion", "ORGANIZADOR/EVENTO_CREAR", UtcNow);
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { asignar, quitar });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto
            {
                FechaDesde = UtcNow.AddDays(-1),
                FechaHasta = UtcNow.AddDays(1),
                Operacion = "ROL_ASIGNAR_ACCION",
            });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("ROL_ASIGNAR_ACCION", fila.Operacion);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_FiltraPorActorUsuarioId()
        {
            var deActor1 = BuildAudit("USUARIO_DESACTIVAR", "actor-1", "Usuario", "usuario-x", UtcNow);
            var deActor2 = BuildAudit("USUARIO_DESACTIVAR", "actor-2", "Usuario", "usuario-x", UtcNow);
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { deActor1, deActor2 });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto
            {
                FechaDesde = UtcNow.AddDays(-1),
                FechaHasta = UtcNow.AddDays(1),
                ActorUsuarioId = "actor-2",
            });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("actor-2", fila.ActorUsuarioId);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_FiltraPorTargetTipo_IncluyeUsuarioRol()
        {
            // UsuarioRol es un cuarto valor real (SecurityAdminService.AsignarRolAUsuarioAsync),
            // no enumerado en el plan original (docs/api-mvp-plan.md §11.3 solo lista
            // Rol/Usuario/RolAccion) pero necesario para poder filtrar la operación más frecuente
            // de /admin/usuarios.
            var asignarRolAUsuario = BuildAudit("USUARIO_ASIGNAR_ROL", "actor-1", "UsuarioRol", "usuario-x/ORGANIZADOR", UtcNow);
            var activarUsuario = BuildAudit("USUARIO_ACTIVAR", "actor-1", "Usuario", "usuario-x", UtcNow);
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { asignarRolAUsuario, activarUsuario });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto
            {
                FechaDesde = UtcNow.AddDays(-1),
                FechaHasta = UtcNow.AddDays(1),
                TargetTipo = SecurityAuditTargetTipo.UsuarioRol,
            });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("UsuarioRol", fila.TargetTipo);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_FiltraPorTargetId_MatchExactoNoSubstring()
        {
            var exacto = BuildAudit("ROL_ASIGNAR_ACCION", "actor-1", "RolAccion", "ORGANIZADOR/EVENTO_CREAR", UtcNow);
            var otro = BuildAudit("ROL_ASIGNAR_ACCION", "actor-1", "RolAccion", "ORGANIZADOR/EVENTO_CREAR_OTRO", UtcNow);
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { exacto, otro });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto
            {
                FechaDesde = UtcNow.AddDays(-1),
                FechaHasta = UtcNow.AddDays(1),
                TargetId = "ORGANIZADOR/EVENTO_CREAR",
            });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("ORGANIZADOR/EVENTO_CREAR", fila.TargetId);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_ResuelveActorEmailEnBatch_NuncaExternalSubjectId()
        {
            var audit = BuildAudit("ROL_CREAR", "usuario-1", "Rol", "ORGANIZADOR", UtcNow);
            var usuario = new Usuario { Id = "usuario-1", Email = "admin@hoydonde.com", ExternalSubjectId = "firebase-uid-secreto" };
            var (sut, _, usuarioRepo) = CreateSut(new List<SecurityAudit> { audit }, new List<Usuario> { usuario });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto { FechaDesde = UtcNow.AddDays(-1), FechaHasta = UtcNow.AddDays(1) });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("admin@hoydonde.com", fila.ActorEmail);
            usuarioRepo.Verify(r => r.GetByIdsAsync(It.Is<IEnumerable<string>>(ids => ids.Contains("usuario-1"))), Times.Once);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_ActorYaNoExiste_ActorEmailEsNull()
        {
            var audit = BuildAudit("ROL_CREAR", "usuario-borrado", "Rol", "ORGANIZADOR", UtcNow);
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { audit }, new List<Usuario>());

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto { FechaDesde = UtcNow.AddDays(-1), FechaHasta = UtcNow.AddDays(1) });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Null(fila.ActorEmail);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_SinRangoInformado_UsaDefaultDe30Dias()
        {
            var (sut, auditRepo, _) = CreateSut(new List<SecurityAudit>());

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto());

            Assert.Equal(30, Math.Round((resultado.FechaHasta - resultado.FechaDesde).TotalDays));
            auditRepo.Verify(r => r.GetByRangoAsync(resultado.FechaDesde, resultado.FechaHasta), Times.Once);
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_RangoExcede366Dias_ThrowsReporteRangoInvalidoException()
        {
            var (sut, _, _) = CreateSut(new List<SecurityAudit>());

            await Assert.ThrowsAsync<ReporteRangoInvalidoException>(() => sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto
            {
                FechaDesde = UtcNow,
                FechaHasta = UtcNow.AddDays(400),
            }));
        }

        [Fact]
        public async Task GetSecurityAuditsReportAsync_NuncaExponeActorPersonaId()
        {
            var audit = BuildAudit("ROL_CREAR", "usuario-1", "Rol", "ORGANIZADOR", UtcNow, "nombre=X");
            var (sut, _, _) = CreateSut(new List<SecurityAudit> { audit });

            var resultado = await sut.GetSecurityAuditsReportAsync(new SecurityAuditReportFilterDto { FechaDesde = UtcNow.AddDays(-1), FechaHasta = UtcNow.AddDays(1) });

            var fila = Assert.Single(resultado.Auditorias);
            Assert.Equal("usuario-1", fila.ActorUsuarioId);
            Assert.DoesNotContain($"persona-de-{audit.ActorUsuarioId}", fila.Detalle);
        }
    }
}
