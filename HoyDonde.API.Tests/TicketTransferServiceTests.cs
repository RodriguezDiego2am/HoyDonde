using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using HoyDonde.API.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Transfer rules against an in-memory store honoring the real store's atomic contract.
    public class TicketTransferServiceTests
    {
        private const string SenderUid = "sender-uid";
        private const string SenderPersona = "persona-sender";
        private const string RecipientEmail = "dest@example.com";
        private const string RecipientPersona = "persona-recipient";
        private const string EventId = "event-1";

        private readonly FakeTicketTransferStore _store = new();
        private readonly Mock<IUsuarioRepository> _usuarios = new();
        private readonly Mock<IPermissionService> _permisos = new();
        private readonly Mock<IAuthenticatedPersonaResolver> _resolver = new();

        private void Arrange(bool recipientActive = true, bool recipientCanHoldTickets = true, bool recipientExists = true)
        {
            _resolver.Setup(r => r.ResolvePersonaIdAsync(SenderUid)).ReturnsAsync(SenderPersona);
            _usuarios.Setup(u => u.GetByEmailAsync(RecipientEmail)).ReturnsAsync(
                recipientExists ? new Usuario { Id = "usuario-recipient", PersonaId = RecipientPersona, Email = RecipientEmail, IsActive = recipientActive } : null);
            _permisos.Setup(p => p.GetPermisosEfectivosPorUsuarioIdAsync("usuario-recipient")).ReturnsAsync(
                new PermisosEfectivosResult("usuario-recipient", RecipientPersona, recipientActive, new[] { "CLIENTE" },
                    recipientCanHoldTickets ? new[] { Acciones.TicketVerPropio } : Array.Empty<string>()));
            _store.SeedEvent(PublishedEvent());

        }

        private static Event PublishedEvent() => new()
        {
            Id = EventId,
            Estado = Event.EventStatus.Publicado,
            FechaInicio = DateTime.UtcNow.AddDays(1),
            FechaFin = DateTime.UtcNow.AddDays(2),
        };

        private Ticket SeedTicket(string id = "ticket-1", string owner = SenderPersona, Ticket.TicketStatus estado = Ticket.TicketStatus.Emitido)
        {
            var ticket = new Ticket
            {
                Id = id,
                EventoId = EventId,
                ClientePersonaId = owner,
                Estado = estado,
                QrToken = QrTokenGenerator.NewToken(),
                QrVersion = 1,
            };
            _store.Seed(ticket);
            return ticket;
        }

        private TicketTransferService Sut() =>
            new(_resolver.Object, _usuarios.Object, _permisos.Object, _store, NullLogger<TicketTransferService>.Instance);

        [Fact]
        public async Task Transfer_HappyPath_MovesOwnerRotatesTokenAndWritesAudit()
        {
            Arrange();
            var ticket = SeedTicket();
            var oldToken = ticket.QrToken;

            var response = await Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail);

            Assert.Equal("ticket-1", response.TicketId);
            Assert.Equal(RecipientPersona, _store.Get("ticket-1").ClientePersonaId);
            Assert.NotEqual(oldToken, _store.Get("ticket-1").QrToken);
            Assert.True(QrTokenGenerator.LooksLikeToken(_store.Get("ticket-1").QrToken));
            Assert.Equal(2, _store.Get("ticket-1").QrVersion);

            var audit = Assert.Single(_store.Transfers);
            Assert.Equal("ticket-1", audit.TicketId);
            Assert.Equal(EventId, audit.EventoId);
            Assert.Equal(SenderPersona, audit.FromPersonaId);
            Assert.Equal(RecipientPersona, audit.ToPersonaId);
            Assert.Equal(1, audit.PreviousQrVersion);
            Assert.Equal(2, audit.NewQrVersion);
        }

        [Fact]
        public async Task Transfer_AuditNeverContainsQrToken()
        {
            Arrange();
            var ticket = SeedTicket();
            var oldToken = ticket.QrToken!;

            await Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail);

            var newToken = _store.Get("ticket-1").QrToken!;
            var serialized = System.Text.Json.JsonSerializer.Serialize(_store.Transfers.Single());
            Assert.DoesNotContain(oldToken, serialized);
            Assert.DoesNotContain(newToken, serialized);
            Assert.DoesNotContain(typeof(TicketTransfer).GetProperties(), p => p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task Transfer_ThenOldQrFailsValidationAndNewQrPasses()
        {
            Arrange();
            var ticket = SeedTicket();
            var oldToken = ticket.QrToken!;
            await Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail);

            var validation = new FakeTicketValidationStore();
            validation.Seed(_store.Get("ticket-1"));
            validation.SeedEvent(PublishedEvent());

            Assert.Equal(TicketConsumeResult.QrInvalido, await validation.TryConsumeAsync(oldToken, EventId, "control"));
            Assert.Equal(TicketConsumeResult.Success, await validation.TryConsumeAsync(_store.Get("ticket-1").QrToken!, EventId, "control"));
        }

        [Fact]
        public async Task Transfer_NotOwner_ThrowsNotFound_AndChangesNothing()
        {
            Arrange();
            SeedTicket(owner: "persona-someone-else");

            await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));

            Assert.Equal("persona-someone-else", _store.Get("ticket-1").ClientePersonaId);
            Assert.Empty(_store.Transfers);
        }

        [Fact]
        public async Task Transfer_UnknownTicket_ThrowsNotFound_SameAsNotOwner()
        {
            Arrange();

            await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "missing", RecipientEmail));
        }

        [Fact]
        public async Task Transfer_NotOwner_NeverTouchesRecipientLookup_NorCountsAFailedLookup()
        {
            Arrange();
            SeedTicket(owner: "persona-someone-else");

            await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
            await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", "nobody@example.com"));

            _usuarios.Verify(u => u.GetByEmailAsync(It.IsAny<string>()), Times.Never);
            _permisos.Verify(p => p.GetPermisosEfectivosPorUsuarioIdAsync(It.IsAny<string>()), Times.Never);
            Assert.Empty(_store.LookupAttempts);
        }

        [Fact]
        public async Task Transfer_NotOwner_RegisteredAndUnregisteredRecipient_AreIndistinguishable()
        {
            Arrange();
            SeedTicket(owner: "persona-someone-else");

            var registered = await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
            var unregistered = await Assert.ThrowsAsync<TicketNoEncontradoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", "nobody@example.com"));

            Assert.Equal(registered.GetType(), unregistered.GetType());
            Assert.Equal(registered.Message, unregistered.Message);
        }

        [Fact]
        public async Task Transfer_NotTransferableTicket_NeverTouchesRecipientLookup()
        {
            Arrange();
            SeedTicket(estado: Ticket.TicketStatus.Usado);

            await Assert.ThrowsAsync<TicketNoTransferibleException>(() => Sut().TransferAsync(SenderUid, "ticket-1", "nobody@example.com"));

            _usuarios.Verify(u => u.GetByEmailAsync(It.IsAny<string>()), Times.Never);
            Assert.Empty(_store.LookupAttempts);
        }

        [Fact]
        public async Task Transfer_FailedLookups_AreCountedAndBlockFurtherProbingBeforeTheLookup()
        {
            Arrange();
            SeedTicket();

            for (var i = 0; i < TicketTransferService.MaxFailedLookupsPerWindow; i++)
                await Assert.ThrowsAsync<TransferenciaDestinatarioInvalidoException>(
                    () => Sut().TransferAsync(SenderUid, "ticket-1", $"nobody{i}@example.com"));

            Assert.Equal(TicketTransferService.MaxFailedLookupsPerWindow, _store.LookupAttempts.Count);
            _usuarios.Invocations.Clear();

            // Over the limit: rejected before the lookup, even for a registered recipient.
            await Assert.ThrowsAsync<TransferenciaLimiteExcedidoException>(
                () => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
            _usuarios.Verify(u => u.GetByEmailAsync(It.IsAny<string>()), Times.Never);
            Assert.Equal(SenderPersona, _store.Get("ticket-1").ClientePersonaId);
            Assert.Empty(_store.Transfers);
        }

        [Fact]
        public async Task Transfer_FailedLookupLimit_IsPerSender_AndIgnoresAttemptsOutsideTheWindow()
        {
            Arrange();
            SeedTicket();
            var window = TicketTransferService.RateLimitWindow;
            for (var i = 0; i < TicketTransferService.MaxFailedLookupsPerWindow; i++)
                _store.LookupAttempts.Add(new TicketTransferLookupAttempt { FromPersonaId = SenderPersona, Timestamp = DateTime.UtcNow - window - TimeSpan.FromMinutes(1 + i) });
            for (var i = 0; i < 20; i++)
                _store.LookupAttempts.Add(new TicketTransferLookupAttempt { FromPersonaId = "persona-other", Timestamp = DateTime.UtcNow });

            await Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail);

            Assert.Equal(RecipientPersona, _store.Get("ticket-1").ClientePersonaId);
        }

        [Fact]
        public async Task Transfer_SuccessfulLookup_ReleasesItsReservation_SoValidTransfersDoNotConsumeTheBudget()
        {
            Arrange();
            SeedTicket();

            await Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail);

            Assert.Empty(_store.LookupAttempts);
        }

        [Fact]
        public async Task Transfer_ToSelf_ReleasesItsReservation()
        {
            Arrange();
            _usuarios.Setup(u => u.GetByEmailAsync("me@example.com")).ReturnsAsync(
                new Usuario { Id = "usuario-recipient", PersonaId = SenderPersona, Email = "me@example.com", IsActive = true });
            SeedTicket();

            await Assert.ThrowsAsync<TransferenciaAUnoMismoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", "me@example.com"));

            Assert.Empty(_store.LookupAttempts);
        }

        [Fact]
        public void LookupAttempt_StoresNoEmailOrTicketData()
        {
            var names = typeof(TicketTransferLookupAttempt).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

            Assert.Equal(new[] { "FromPersonaId", "Id", "Timestamp" }, names);
        }

        [Theory]
        [InlineData(Ticket.TicketStatus.Usado)]
        [InlineData(Ticket.TicketStatus.Anulado)]
        public async Task Transfer_UsedOrAnnulledTicket_ThrowsNotTransferable(Ticket.TicketStatus estado)
        {
            Arrange();
            SeedTicket(estado: estado);

            await Assert.ThrowsAsync<TicketNoTransferibleException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));

            Assert.Equal(SenderPersona, _store.Get("ticket-1").ClientePersonaId);
            Assert.Empty(_store.Transfers);
        }

        [Fact]
        public async Task Transfer_CancelledEvent_ThrowsNotTransferable()
        {
            Arrange();
            SeedTicket();
            var cancelled = PublishedEvent();
            cancelled.Estado = Event.EventStatus.Cancelado;
            _store.SeedEvent(cancelled);

            await Assert.ThrowsAsync<TicketNoTransferibleException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
        }

        [Fact]
        public async Task Transfer_FinishedEvent_ThrowsNotTransferable()
        {
            Arrange();
            SeedTicket();
            var finished = PublishedEvent();
            finished.FechaInicio = DateTime.UtcNow.AddDays(-3);
            finished.FechaFin = DateTime.UtcNow.AddDays(-1);
            _store.SeedEvent(finished);

            await Assert.ThrowsAsync<TicketNoTransferibleException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
        }

        [Fact]
        public async Task Transfer_ToSelf_Throws_AndChangesNothing()
        {
            Arrange();
            _usuarios.Setup(u => u.GetByEmailAsync("me@example.com")).ReturnsAsync(
                new Usuario { Id = "usuario-recipient", PersonaId = SenderPersona, Email = "me@example.com", IsActive = true });
            SeedTicket();

            await Assert.ThrowsAsync<TransferenciaAUnoMismoException>(() => Sut().TransferAsync(SenderUid, "ticket-1", "me@example.com"));

            Assert.Empty(_store.Transfers);
        }

        [Theory]
        [InlineData(false, true, true)]   // unknown email
        [InlineData(true, false, true)]   // inactive account
        [InlineData(true, true, false)]   // account that cannot hold tickets
        public async Task Transfer_InvalidRecipient_ThrowsSingleGenericException_AndChangesNothing(bool exists, bool active, bool canHold)
        {
            Arrange(recipientExists: exists, recipientActive: active, recipientCanHoldTickets: canHold);
            SeedTicket();

            var ex = await Assert.ThrowsAsync<TransferenciaDestinatarioInvalidoException>(
                () => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));

            Assert.DoesNotContain("example.com", ex.Message);
            Assert.Equal(SenderPersona, _store.Get("ticket-1").ClientePersonaId);
            Assert.Empty(_store.Transfers);
        }

        [Fact]
        public async Task Transfer_RateLimit_AllowsTenThenRejectsEleventh()
        {
            Arrange();
            for (var i = 0; i < 11; i++) SeedTicket($"ticket-{i}");

            for (var i = 0; i < TicketTransferService.MaxTransfersPerWindow; i++)
                await Sut().TransferAsync(SenderUid, $"ticket-{i}", RecipientEmail);

            await Assert.ThrowsAsync<TransferenciaLimiteExcedidoException>(
                () => Sut().TransferAsync(SenderUid, "ticket-10", RecipientEmail));

            Assert.Equal(SenderPersona, _store.Get("ticket-10").ClientePersonaId);
            Assert.Equal(10, _store.Transfers.Count);
        }

        [Fact]
        public async Task Transfer_ActorNotProvisioned_Propagates()
        {
            Arrange();
            _resolver.Setup(r => r.ResolvePersonaIdAsync(SenderUid)).ThrowsAsync(new IdentityNotProvisionedException(SenderUid));
            SeedTicket();

            await Assert.ThrowsAsync<IdentityNotProvisionedException>(() => Sut().TransferAsync(SenderUid, "ticket-1", RecipientEmail));
        }

        [Fact]
        public void QrTokenGenerator_ProducesDistinctUrlSafe128BitTokens()
        {
            var tokens = Enumerable.Range(0, 200).Select(_ => QrTokenGenerator.NewToken()).ToList();

            Assert.Equal(200, tokens.Distinct().Count());
            Assert.All(tokens, t =>
            {
                Assert.Equal(QrTokenGenerator.EncodedLength, t.Length);
                Assert.True(QrTokenGenerator.LooksLikeToken(t));
                Assert.DoesNotContain('=', t);
            });
        }
    }
}
