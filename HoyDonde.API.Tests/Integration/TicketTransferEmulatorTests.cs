using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Commands;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Free ticket transfer against the real Firestore Emulator: transactional store, HTTP pipeline,
    // token rotation vs door validation, audit content, rate limit, and the QR token migration.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class TicketTransferEmulatorTests
    {
        private static readonly JsonSerializerOptions EnumAwareJson = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
        };

        private readonly FirestoreEmulatorFixture _fixture;

        public TicketTransferEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        // ---- helpers ----

        private async Task<(string Uid, string PersonaId, string UsuarioId, string Email)> SeedActorAsync(string rolCodigo)
        {
            var repository = new FirestoreUsuarioRepository(_fixture.Db!);
            var uid = $"uid-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var personaId = $"persona-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var email = $"{uid}@test.hoydonde.local";
            await repository.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, "FIREBASE", uid, email, rolCodigo, "test-seed"));
            return (uid, personaId, usuarioId, email);
        }

        private async Task SeedCatalogAsync() =>
            await new SecurityCatalogSeeder(new FirestoreRolRepository(_fixture.Db!), new FirestoreAccionRepository(_fixture.Db!)).SeedAsync();

        private async Task<string> SeedEventAsync(DateTime? inicio = null, DateTime? fin = null, Event.EventStatus estado = Event.EventStatus.Publicado)
        {
            var eventId = $"event-{Guid.NewGuid():N}";
            await _fixture.Db!.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento transferible",
                FechaInicio = inicio ?? DateTime.UtcNow.AddHours(-1),
                FechaFin = fin ?? DateTime.UtcNow.AddHours(3),
                Estado = estado,
            });
            return eventId;
        }

        private async Task<Ticket> SeedTicketAsync(string eventId, string ownerPersonaId, Ticket.TicketStatus estado = Ticket.TicketStatus.Emitido, int qrVersion = 1)
        {
            var ticket = new Ticket
            {
                Id = $"ticket-{Guid.NewGuid():N}",
                EventoId = eventId,
                ClientePersonaId = ownerPersonaId,
                Estado = estado,
                QrToken = QrTokenGenerator.NewToken(),
                QrVersion = qrVersion,
                FechaInicio = DateTime.UtcNow.AddHours(-1),
                FechaFin = DateTime.UtcNow.AddHours(3),
            };
            await _fixture.Db!.Collection("tickets").Document(ticket.Id).SetAsync(ticket);
            return ticket;
        }

        private async Task<Ticket> ReadTicketAsync(string ticketId) =>
            (await _fixture.Db!.Collection("tickets").Document(ticketId).GetSnapshotAsync()).ConvertTo<Ticket>();

        private static string StripTrace(string body) =>
            System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"-\"");

        private async Task<List<TicketTransferLookupAttempt>> ReadLookupAttemptsAsync(string fromPersonaId) =>
            (await _fixture.Db!.Collection("ticket_transfer_lookup_attempts").WhereEqualTo(nameof(TicketTransferLookupAttempt.FromPersonaId), fromPersonaId).GetSnapshotAsync())
                .Documents.Select(d => d.ConvertTo<TicketTransferLookupAttempt>()).ToList();

        private static TicketTransferPrecheckRequest Precheck(string ticketId, string from, int max = 10) =>
            new(ticketId, from, max, TimeSpan.FromHours(1));

        private static TicketTransferRequest Request(string ticketId, string from, string to, int max = 10) =>
            new(ticketId, from, to, QrTokenGenerator.NewToken(), max, TimeSpan.FromHours(1));

        private async Task<List<TicketTransfer>> ReadTransfersAsync(string ticketId) =>
            (await _fixture.Db!.Collection("ticket_transfers").WhereEqualTo(nameof(TicketTransfer.TicketId), ticketId).GetSnapshotAsync())
                .Documents.Select(d => d.ConvertTo<TicketTransfer>()).ToList();

        // ---- store: transactional rules ----

        [FirestoreEmulatorFact]
        public async Task Store_Transfer_MovesOwner_RotatesToken_BumpsVersion_AndWritesAuditWithoutToken()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var from = $"persona-from-{Guid.NewGuid():N}";
            var to = $"persona-to-{Guid.NewGuid():N}";
            var ticket = await SeedTicketAsync(eventId, from);
            var request = Request(ticket.Id, from, to);

            var result = await store.TryTransferAsync(request);

            Assert.Equal(TicketTransferStatus.Success, result.Status);
            Assert.Equal(2, result.NewQrVersion);
            var persisted = await ReadTicketAsync(ticket.Id);
            Assert.Equal(to, persisted.ClientePersonaId);
            Assert.Equal(request.NewQrToken, persisted.QrToken);
            Assert.NotEqual(ticket.QrToken, persisted.QrToken);
            Assert.Equal(2, persisted.QrVersion);
            Assert.Equal(Ticket.TicketStatus.Emitido, persisted.Estado);

            var audits = await ReadTransfersAsync(ticket.Id);
            var audit = Assert.Single(audits);
            Assert.Equal(eventId, audit.EventoId);
            Assert.Equal(from, audit.FromPersonaId);
            Assert.Equal(to, audit.ToPersonaId);
            Assert.Equal(1, audit.PreviousQrVersion);
            Assert.Equal(2, audit.NewQrVersion);

            // The raw audit document must not contain any token, old or new.
            var raw = (await _fixture.Db!.Collection("ticket_transfers").Document(audit.Id).GetSnapshotAsync()).ToDictionary();
            var rawText = string.Join("|", raw.Select(kv => $"{kv.Key}={kv.Value}"));
            Assert.DoesNotContain(ticket.QrToken!, rawText);
            Assert.DoesNotContain(request.NewQrToken, rawText);
            Assert.DoesNotContain(raw.Keys, k => k.Contains("Token", StringComparison.OrdinalIgnoreCase));
        }

        [FirestoreEmulatorFact]
        public async Task Store_Transfer_NotOwner_OrMissing_ReturnsNotFound_AndWritesNothing()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner");

            var notOwner = await store.TryTransferAsync(Request(ticket.Id, "persona-intruder", "persona-x"));
            var missing = await store.TryTransferAsync(Request($"missing-{Guid.NewGuid():N}", "persona-owner", "persona-x"));

            Assert.Equal(TicketTransferStatus.NotFound, notOwner.Status);
            Assert.Equal(TicketTransferStatus.NotFound, missing.Status);
            var persisted = await ReadTicketAsync(ticket.Id);
            Assert.Equal("persona-owner", persisted.ClientePersonaId);
            Assert.Equal(ticket.QrToken, persisted.QrToken);
            Assert.Empty(await ReadTransfersAsync(ticket.Id));
        }

        [FirestoreEmulatorTheory]
        [InlineData(Ticket.TicketStatus.Usado)]
        [InlineData(Ticket.TicketStatus.Anulado)]
        public async Task Store_Transfer_UsedOrAnnulled_ReturnsNotTransferable(Ticket.TicketStatus estado)
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner", estado);

            var result = await store.TryTransferAsync(Request(ticket.Id, "persona-owner", "persona-x"));

            Assert.Equal(TicketTransferStatus.NotTransferable, result.Status);
            Assert.Equal("persona-owner", (await ReadTicketAsync(ticket.Id)).ClientePersonaId);
            Assert.Empty(await ReadTransfersAsync(ticket.Id));
        }

        [FirestoreEmulatorFact]
        public async Task Store_Transfer_CancelledOrFinishedEvent_ReturnsNotTransferable()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var cancelledEvent = await SeedEventAsync(estado: Event.EventStatus.Cancelado);
            var finishedEvent = await SeedEventAsync(inicio: DateTime.UtcNow.AddDays(-3), fin: DateTime.UtcNow.AddDays(-1));
            var cancelledTicket = await SeedTicketAsync(cancelledEvent, "persona-owner");
            var finishedTicket = await SeedTicketAsync(finishedEvent, "persona-owner");

            var cancelled = await store.TryTransferAsync(Request(cancelledTicket.Id, "persona-owner", "persona-x"));
            var finished = await store.TryTransferAsync(Request(finishedTicket.Id, "persona-owner", "persona-x"));

            Assert.Equal(TicketTransferStatus.NotTransferable, cancelled.Status);
            Assert.Equal(TicketTransferStatus.NotTransferable, finished.Status);
            Assert.Equal("persona-owner", (await ReadTicketAsync(cancelledTicket.Id)).ClientePersonaId);
            Assert.Equal("persona-owner", (await ReadTicketAsync(finishedTicket.Id)).ClientePersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task Store_Transfer_LegacyTicketWithoutToken_GetsFirstTokenAndVersionTwo()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner", qrVersion: 0);
            await _fixture.Db!.Collection("tickets").Document(ticket.Id).UpdateAsync(nameof(Ticket.QrToken), FieldValue.Delete);

            var request = Request(ticket.Id, "persona-owner", "persona-x");
            var result = await store.TryTransferAsync(request);

            Assert.Equal(TicketTransferStatus.Success, result.Status);
            var persisted = await ReadTicketAsync(ticket.Id);
            Assert.Equal(request.NewQrToken, persisted.QrToken);
            Assert.Equal(2, persisted.QrVersion);
            Assert.Equal(0, (await ReadTransfersAsync(ticket.Id)).Single().PreviousQrVersion);
        }

        [FirestoreEmulatorFact]
        public async Task Store_RateLimit_RejectsBeyondMaxWithinWindow_IgnoresOldRows_AndIsPerSender()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var sender = $"persona-busy-{Guid.NewGuid():N}";
            var otherSender = $"persona-calm-{Guid.NewGuid():N}";

            // 3 recent transfers by the sender (max for this test = 3) and 5 old ones that must not count.
            for (var i = 0; i < 3; i++)
                await _fixture.Db!.Collection("ticket_transfers").AddAsync(new TicketTransfer { FromPersonaId = sender, Timestamp = DateTime.UtcNow.AddMinutes(-5 - i) });
            for (var i = 0; i < 5; i++)
                await _fixture.Db!.Collection("ticket_transfers").AddAsync(new TicketTransfer { FromPersonaId = sender, Timestamp = DateTime.UtcNow.AddHours(-2 - i) });

            var blockedTicket = await SeedTicketAsync(eventId, sender);
            var blocked = await store.TryTransferAsync(Request(blockedTicket.Id, sender, "persona-x", max: 3));
            Assert.Equal(TicketTransferStatus.RateLimited, blocked.Status);
            Assert.Equal(sender, (await ReadTicketAsync(blockedTicket.Id)).ClientePersonaId);
            Assert.Empty(await ReadTransfersAsync(blockedTicket.Id));

            var allowedTicket = await SeedTicketAsync(eventId, sender);
            Assert.Equal(TicketTransferStatus.Success, (await store.TryTransferAsync(Request(allowedTicket.Id, sender, "persona-x", max: 4))).Status);

            var otherTicket = await SeedTicketAsync(eventId, otherSender);
            Assert.Equal(TicketTransferStatus.Success, (await store.TryTransferAsync(Request(otherTicket.Id, otherSender, "persona-x", max: 3))).Status);
        }

        [FirestoreEmulatorFact]
        public async Task Store_Precheck_NotOwnerMissingOrNotTransferable_ReservesNothing()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var cancelledEvent = await SeedEventAsync(estado: Event.EventStatus.Cancelado);
            var sender = $"persona-probe-{Guid.NewGuid():N}";
            var owned = await SeedTicketAsync(eventId, "persona-owner");
            var used = await SeedTicketAsync(eventId, sender, Ticket.TicketStatus.Usado);
            var cancelled = await SeedTicketAsync(cancelledEvent, sender);

            Assert.Equal(TicketTransferStatus.NotFound, (await store.PrecheckAndReserveLookupAsync(Precheck(owned.Id, sender))).Status);
            Assert.Equal(TicketTransferStatus.NotFound, (await store.PrecheckAndReserveLookupAsync(Precheck("missing-" + Guid.NewGuid().ToString("N"), sender))).Status);
            Assert.Equal(TicketTransferStatus.NotTransferable, (await store.PrecheckAndReserveLookupAsync(Precheck(used.Id, sender))).Status);
            Assert.Equal(TicketTransferStatus.NotTransferable, (await store.PrecheckAndReserveLookupAsync(Precheck(cancelled.Id, sender))).Status);

            Assert.Empty(await ReadLookupAttemptsAsync(sender));
        }

        [FirestoreEmulatorFact]
        public async Task Store_Precheck_ReservesLookupsUpToTheLimit_StoresNoEmail_ReleaseFreesTheSlot_AndIsPerSender()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var sender = $"persona-prober-{Guid.NewGuid():N}";
            var other = $"persona-calm-{Guid.NewGuid():N}";
            var ticket = await SeedTicketAsync(eventId, sender);
            var otherTicket = await SeedTicketAsync(eventId, other);

            // An attempt outside the window must not count.
            await _fixture.Db!.Collection("ticket_transfer_lookup_attempts").AddAsync(
                new TicketTransferLookupAttempt { FromPersonaId = sender, Timestamp = DateTime.UtcNow.AddHours(-3) });

            var first = await store.PrecheckAndReserveLookupAsync(Precheck(ticket.Id, sender, max: 2));
            var second = await store.PrecheckAndReserveLookupAsync(Precheck(ticket.Id, sender, max: 2));
            var third = await store.PrecheckAndReserveLookupAsync(Precheck(ticket.Id, sender, max: 2));

            Assert.Equal(TicketTransferStatus.Success, first.Status);
            Assert.Equal(TicketTransferStatus.Success, second.Status);
            Assert.Equal(TicketTransferStatus.RateLimited, third.Status);
            Assert.Null(third.LookupReservationId);

            // Only the sender id and a timestamp are persisted.
            var raw = (await _fixture.Db!.Collection("ticket_transfer_lookup_attempts").Document(first.LookupReservationId!).GetSnapshotAsync()).ToDictionary();
            Assert.Equal(new[] { "FromPersonaId", "Timestamp" }, raw.Keys.OrderBy(k => k).ToArray());

            // Another sender is unaffected.
            Assert.Equal(TicketTransferStatus.Success, (await store.PrecheckAndReserveLookupAsync(Precheck(otherTicket.Id, other, max: 2))).Status);

            // Releasing a reservation frees its slot.
            await store.ReleaseLookupAsync(first.LookupReservationId!);
            Assert.Equal(TicketTransferStatus.Success, (await store.PrecheckAndReserveLookupAsync(Precheck(ticket.Id, sender, max: 2))).Status);
        }

        [FirestoreEmulatorFact]
        public async Task Store_Precheck_ConcurrentProbes_NeverExceedTheLimit()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var sender = $"persona-burst-{Guid.NewGuid():N}";
            var ticket = await SeedTicketAsync(eventId, sender);

            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                Task.Run(() => store.PrecheckAndReserveLookupAsync(Precheck(ticket.Id, sender, max: 3)))));

            Assert.Equal(3, results.Count(r => r.Status == TicketTransferStatus.Success));
            Assert.Equal(5, results.Count(r => r.Status == TicketTransferStatus.RateLimited));
            Assert.Equal(3, (await ReadLookupAttemptsAsync(sender)).Count);
        }

        [FirestoreEmulatorFact]
        public async Task Http_Transfer_FailedRecipientLookups_AreLimitedPerSender_BeforeTheLookup()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var owner = await SeedActorAsync("CLIENTE");
            var recipient = await SeedActorAsync("CLIENTE");
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, owner.PersonaId);

            // Pre-fill the sender's failed-lookup budget (10/hour).
            for (var i = 0; i < TicketTransferService.MaxFailedLookupsPerWindow; i++)
                await db.Collection("ticket_transfer_lookup_attempts").AddAsync(
                    new TicketTransferLookupAttempt { FromPersonaId = owner.PersonaId, Timestamp = DateTime.UtcNow.AddMinutes(-1) });

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            async Task<HttpResponseMessage> Transfer(string email)
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticket.Id}/transfer")
                {
                    Content = JsonContent.Create(new { recipientEmail = email })
                };
                msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
                msg.Headers.Add("Test-Uid", owner.Uid);
                return await client.SendAsync(msg);
            }

            // Unknown AND registered recipients are both refused with the same 429 before any lookup.
            var unknown = await Transfer("nobody.here@example.com");
            var registered = await Transfer(recipient.Email);
            Assert.Equal((HttpStatusCode)429, unknown.StatusCode);
            Assert.Equal((HttpStatusCode)429, registered.StatusCode);
            Assert.Contains("TRANSFER_RATE_LIMITED", await unknown.Content.ReadAsStringAsync());
            Assert.Contains("TRANSFER_RATE_LIMITED", await registered.Content.ReadAsStringAsync());
            Assert.Equal(owner.PersonaId, (await ReadTicketAsync(ticket.Id)).ClientePersonaId);
        }

        [FirestoreEmulatorFact]
        public async Task Http_Transfer_UnknownRecipientIsCountedAsFailedLookup_ValidTransferIsNot()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var owner = await SeedActorAsync("CLIENTE");
            var recipient = await SeedActorAsync("CLIENTE");
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, owner.PersonaId);

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            async Task<HttpResponseMessage> Transfer(string email)
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticket.Id}/transfer")
                {
                    Content = JsonContent.Create(new { recipientEmail = email })
                };
                msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
                msg.Headers.Add("Test-Uid", owner.Uid);
                return await client.SendAsync(msg);
            }

            Assert.Equal(HttpStatusCode.NotFound, (await Transfer("nobody.here@example.com")).StatusCode);
            Assert.Single(await ReadLookupAttemptsAsync(owner.PersonaId));

            Assert.Equal(HttpStatusCode.OK, (await Transfer(recipient.Email)).StatusCode);
            Assert.Single(await ReadLookupAttemptsAsync(owner.PersonaId));
        }

        [FirestoreEmulatorFact]
        public async Task Store_ConcurrentTransfersOfSameTicket_ExactlyOneWins()
        {
            var store = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner");

            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
                Task.Run(() => store.TryTransferAsync(Request(ticket.Id, "persona-owner", $"persona-dest-{i}")))));

            Assert.Single(results, r => r.Status == TicketTransferStatus.Success);
            Assert.Equal(3, results.Count(r => r.Status == TicketTransferStatus.NotFound));
            var audit = Assert.Single(await ReadTransfersAsync(ticket.Id));
            Assert.Equal((await ReadTicketAsync(ticket.Id)).ClientePersonaId, audit.ToPersonaId);
        }

        // ---- transfer vs door validation ----

        [FirestoreEmulatorFact]
        public async Task TransferVsValidation_NeverBothSucceed_AndOldQrNeverValidatesAfterTransfer()
        {
            var transferStore = new FirestoreTicketTransferStore(_fixture.Db!);
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var eventId = await SeedEventAsync();

            for (var round = 0; round < 6; round++)
            {
                var ticket = await SeedTicketAsync(eventId, "persona-owner");
                var oldToken = ticket.QrToken!;

                var transferTask = Task.Run(() => transferStore.TryTransferAsync(Request(ticket.Id, "persona-owner", "persona-dest")));
                var validateTask = Task.Run(() => validationStore.TryConsumeAsync(oldToken, eventId, "control-persona"));
                var transfer = await transferTask;
                var validation = await validateTask;

                var persisted = await ReadTicketAsync(ticket.Id);
                if (transfer.Status == TicketTransferStatus.Success)
                {
                    // The transfer won: the already-shown QR must be rejected, the ticket stays unused.
                    Assert.NotEqual(TicketConsumeResult.Success, validation);
                    Assert.Equal(Ticket.TicketStatus.Emitido, persisted.Estado);
                    Assert.Equal("persona-dest", persisted.ClientePersonaId);
                    Assert.Equal(TicketConsumeResult.QrInvalido, await validationStore.TryConsumeAsync(oldToken, eventId, "control-persona"));
                }
                else
                {
                    // Validation won: the used ticket can no longer be transferred.
                    Assert.Equal(TicketTransferStatus.NotTransferable, transfer.Status);
                    Assert.Equal(TicketConsumeResult.Success, validation);
                    Assert.Equal(Ticket.TicketStatus.Usado, persisted.Estado);
                    Assert.Equal("persona-owner", persisted.ClientePersonaId);
                    Assert.Empty(await ReadTransfersAsync(ticket.Id));
                }
            }
        }

        [FirestoreEmulatorFact]
        public async Task Validation_ByToken_UnknownAndStaleTokensAreRejected_CurrentTokenConsumesOnce()
        {
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var transferStore = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner");

            Assert.Equal(TicketConsumeResult.QrInvalido, await validationStore.TryConsumeAsync(QrTokenGenerator.NewToken(), eventId, "control"));

            var transfer = Request(ticket.Id, "persona-owner", "persona-dest");
            await transferStore.TryTransferAsync(transfer);

            Assert.Equal(TicketConsumeResult.QrInvalido, await validationStore.TryConsumeAsync(ticket.QrToken!, eventId, "control"));
            Assert.Equal(TicketConsumeResult.EventMismatch, await validationStore.TryConsumeAsync(transfer.NewQrToken, "another-event", "control"));
            Assert.Equal(TicketConsumeResult.Success, await validationStore.TryConsumeAsync(transfer.NewQrToken, eventId, "control"));
            Assert.Equal(TicketConsumeResult.AlreadyUsed, await validationStore.TryConsumeAsync(transfer.NewQrToken, eventId, "control"));
        }

        [FirestoreEmulatorFact]
        public async Task Validation_FindEventIdByToken_ResolvesCurrentTokenOnly()
        {
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var transferStore = new FirestoreTicketTransferStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner");

            Assert.Equal(eventId, await validationStore.FindEventIdByQrTokenAsync(ticket.QrToken!));
            Assert.Null(await validationStore.FindEventIdByQrTokenAsync(QrTokenGenerator.NewToken()));

            await transferStore.TryTransferAsync(Request(ticket.Id, "persona-owner", "persona-dest"));
            Assert.Null(await validationStore.FindEventIdByQrTokenAsync(ticket.QrToken!));
        }

        [FirestoreEmulatorFact]
        public async Task Validation_LegacyTicketWithoutToken_CannotBeValidatedByTicketId()
        {
            var validationStore = new FirestoreTicketValidationStore(_fixture.Db!);
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, "persona-owner", qrVersion: 0);
            await _fixture.Db!.Collection("tickets").Document(ticket.Id).UpdateAsync(nameof(Ticket.QrToken), FieldValue.Delete);

            // The old QR carried the ticket id; neither it nor an empty token reaches the ticket.
            Assert.Equal(TicketConsumeResult.QrInvalido, await validationStore.TryConsumeAsync(ticket.Id, eventId, "control"));
            Assert.Equal(TicketConsumeResult.QrInvalido, await validationStore.TryConsumeAsync(string.Empty, eventId, "control"));
            Assert.Equal(Ticket.TicketStatus.Emitido, (await ReadTicketAsync(ticket.Id)).Estado);
        }

        // ---- end to end over HTTP ----

        [FirestoreEmulatorFact]
        public async Task Http_BuyTransferValidate_OldQrRejected_NewOwnerSeesNewTokenAndNoPrice_PurchaserStaysOnCompra()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var organizador = await SeedActorAsync("ORGANIZADOR");
            var seller = await SeedActorAsync("CLIENTE");
            var buyer = await SeedActorAsync("CLIENTE");
            var control = await SeedActorAsync("CONTROL");

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            HttpRequestMessage Req(HttpMethod method, string path, string uid, object? body = null)
            {
                var msg = new HttpRequestMessage(method, path);
                msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
                msg.Headers.Add("Test-Uid", uid);
                if (body != null) msg.Content = JsonContent.Create(body, options: EnumAwareJson);
                return msg;
            }

            var createResponse = await client.SendAsync(Req(HttpMethod.Post, "/api/events", organizador.Uid, new EventCreateRequest
            {
                Nombre = "Reventa fase 1",
                Descripcion = "Transferencia gratuita",
                FechaInicio = DateTime.UtcNow.AddHours(2),
                FechaFin = DateTime.UtcNow.AddHours(4),
                Ubicacion = "Sala",
                Categoria = Event.EventCategory.Musica,
                TicketGroups = new List<TicketGroupDto> { new() { Nombre = "General", Precio = 250m, CantidadDisponible = 5 } },
            }));
            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
            var created = (await createResponse.Content.ReadFromJsonAsync<EventResponse>(EnumAwareJson))!;
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req(HttpMethod.Post, $"/api/events/{created.Id}/publish", organizador.Uid))).StatusCode);
            await new FirestoreControlAsignacionRepository(db).AsignarAsync(control.PersonaId, created.Id, organizador.PersonaId);

            // The seller buys: the purchase response carries the QR token for the owner.
            var buyResponse = await client.SendAsync(Req(HttpMethod.Post, "/api/tickets/buy", seller.Uid,
                new TicketBuyRequest { EventoId = created.Id, TicketTypeId = created.TicketGroups[0].Id, Cantidad = 1 }));
            Assert.Equal(HttpStatusCode.OK, buyResponse.StatusCode);
            var compra = (await buyResponse.Content.ReadFromJsonAsync<CompraResponseDto>())!;
            var ticketId = compra.Tickets[0].Id;
            var oldToken = compra.Tickets[0].QrToken!;
            Assert.True(QrTokenGenerator.LooksLikeToken(oldToken));
            Assert.Equal(250m, compra.Tickets[0].PrecioPagado);

            // Transfer to the buyer by email.
            var transferResponse = await client.SendAsync(Req(HttpMethod.Post, $"/api/tickets/{ticketId}/transfer", seller.Uid,
                new { recipientEmail = buyer.Email }));
            var transferBody = await transferResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, transferResponse.StatusCode);
            Assert.DoesNotContain(buyer.Email, transferBody);

            // Seller no longer sees it; the new owner sees a different token and no price / compra.
            var sellerTickets = (await (await client.SendAsync(Req(HttpMethod.Get, "/api/tickets/me", seller.Uid)))
                .Content.ReadFromJsonAsync<List<TicketResponseDto>>())!;
            Assert.DoesNotContain(sellerTickets, t => t.Id == ticketId);

            var buyerTickets = (await (await client.SendAsync(Req(HttpMethod.Get, "/api/tickets/me", buyer.Uid)))
                .Content.ReadFromJsonAsync<List<TicketResponseDto>>())!;
            var received = Assert.Single(buyerTickets, t => t.Id == ticketId);
            Assert.Equal(buyer.PersonaId, received.ClientePersonaId);
            Assert.True(QrTokenGenerator.LooksLikeToken(received.QrToken));
            Assert.NotEqual(oldToken, received.QrToken);
            Assert.Null(received.PrecioPagado);
            Assert.Null(received.CompraId);

            // Door: the old QR is rejected, the new one is accepted exactly once.
            var oldScan = await client.SendAsync(Req(HttpMethod.Post, "/api/tickets/validate", control.Uid, new { qrToken = oldToken, eventId = created.Id }));
            Assert.Equal(HttpStatusCode.Conflict, oldScan.StatusCode);
            Assert.Contains("El código QR no es válido o fue reemplazado.", await oldScan.Content.ReadAsStringAsync());

            // The scanner sends only the token: the event is derived from the ticket that owns it.
            var newScan = await client.SendAsync(Req(HttpMethod.Post, "/api/tickets/validate", control.Uid, new { qrToken = received.QrToken }));
            Assert.Equal(HttpStatusCode.OK, newScan.StatusCode);
            var secondScan = await client.SendAsync(Req(HttpMethod.Post, "/api/tickets/validate", control.Uid, new { qrToken = received.QrToken, eventId = created.Id }));
            Assert.Equal(HttpStatusCode.Conflict, secondScan.StatusCode);

            // Persistence: the original purchaser stays on Compra, the owner moved, audit exists.
            var persistedCompra = (await db.Collection("compras").Document(compra.Id).GetSnapshotAsync()).ConvertTo<Compra>();
            Assert.Equal(seller.PersonaId, persistedCompra.ClientePersonaId);
            var persistedTicket = await ReadTicketAsync(ticketId);
            Assert.Equal(buyer.PersonaId, persistedTicket.ClientePersonaId);
            var audit = Assert.Single(await ReadTransfersAsync(ticketId));
            Assert.Equal(seller.PersonaId, audit.FromPersonaId);
            Assert.Equal(buyer.PersonaId, audit.ToPersonaId);

            // The used ticket cannot be transferred again by its new owner.
            var again = await client.SendAsync(Req(HttpMethod.Post, $"/api/tickets/{ticketId}/transfer", buyer.Uid,
                new { recipientEmail = seller.Email }));
            Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        }

        [FirestoreEmulatorFact]
        public async Task Http_Transfer_SelfUnknownRecipientAndNotOwner_AreRejectedWithoutLeakingTheEmail()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var owner = await SeedActorAsync("CLIENTE");
            var stranger = await SeedActorAsync("CLIENTE");
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, owner.PersonaId);

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            HttpRequestMessage Transfer(string uid, string email)
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticket.Id}/transfer")
                {
                    Content = JsonContent.Create(new { recipientEmail = email })
                };
                msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
                msg.Headers.Add("Test-Uid", uid);
                return msg;
            }

            var self = await client.SendAsync(Transfer(owner.Uid, owner.Email));
            Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
            Assert.Contains("TRANSFER_TO_SELF", await self.Content.ReadAsStringAsync());

            const string unknownEmail = "nobody.here@example.com";
            var unknown = await client.SendAsync(Transfer(owner.Uid, unknownEmail));
            var unknownBody = await unknown.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
            Assert.Contains("TRANSFER_RECIPIENT_INVALID", unknownBody);
            Assert.DoesNotContain("nobody.here", unknownBody);

            // Ownership is checked BEFORE the recipient lookup: a caller that does not own the ticket
            // gets the same TICKET_NOT_FOUND whether the email is registered or not (no oracle).
            var notOwnerRegistered = await client.SendAsync(Transfer(stranger.Uid, owner.Email));
            var notOwnerUnregistered = await client.SendAsync(Transfer(stranger.Uid, unknownEmail));
            var registeredBody = await notOwnerRegistered.Content.ReadAsStringAsync();
            var unregisteredBody = await notOwnerUnregistered.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, notOwnerRegistered.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, notOwnerUnregistered.StatusCode);
            Assert.Contains("TICKET_NOT_FOUND", registeredBody);
            Assert.Contains("TICKET_NOT_FOUND", unregisteredBody);
            Assert.Equal(StripTrace(registeredBody), StripTrace(unregisteredBody));

            // And a missing ticket id answers the same way.
            var missingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/does-not-exist/transfer")
            {
                Content = JsonContent.Create(new { recipientEmail = unknownEmail })
            };
            missingRequest.Headers.Authorization = new AuthenticationHeaderValue("Test");
            missingRequest.Headers.Add("Test-Uid", stranger.Uid);
            var missing = await client.SendAsync(missingRequest);
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Contains("TICKET_NOT_FOUND", await missing.Content.ReadAsStringAsync());

            // None of the stranger's probes reserved a lookup attempt.
            Assert.Empty(await ReadLookupAttemptsAsync(stranger.PersonaId));

            Assert.Equal(owner.PersonaId, (await ReadTicketAsync(ticket.Id)).ClientePersonaId);
            Assert.Empty(await ReadTransfersAsync(ticket.Id));
        }

        [FirestoreEmulatorFact]
        public async Task Http_Transfer_RequiresTicketTransferirAction()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var control = await SeedActorAsync("CONTROL");
            var eventId = await SeedEventAsync();
            var ticket = await SeedTicketAsync(eventId, control.PersonaId);

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/tickets/{ticket.Id}/transfer")
            {
                Content = JsonContent.Create(new { recipientEmail = "x@example.com" })
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", control.Uid);

            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(msg)).StatusCode);
        }

        // ---- buy path ----

        [FirestoreEmulatorFact]
        public async Task GetTickets_ForPurchaser_ShowsPriceAndCompra_AndLegacyTransferredTicketHidesPrice()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var cliente = await SeedActorAsync("CLIENTE");
            var eventId = await SeedEventAsync();
            var legacyOwn = await SeedTicketAsync(eventId, cliente.PersonaId, qrVersion: 1);
            var legacyReceived = await SeedTicketAsync(eventId, cliente.PersonaId, qrVersion: 2);
            await db.Collection("tickets").Document(legacyOwn.Id).UpdateAsync(nameof(Ticket.PrecioPagado), 80.0);
            await db.Collection("tickets").Document(legacyReceived.Id).UpdateAsync(nameof(Ticket.PrecioPagado), 90.0);

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var msg = new HttpRequestMessage(HttpMethod.Get, "/api/tickets/me");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", cliente.Uid);
            var tickets = (await (await client.SendAsync(msg)).Content.ReadFromJsonAsync<List<TicketResponseDto>>())!;

            Assert.NotNull(tickets.Single(t => t.Id == legacyOwn.Id).PrecioPagado);
            Assert.Null(tickets.Single(t => t.Id == legacyReceived.Id).PrecioPagado);
        }

        [FirestoreEmulatorFact]
        public async Task BuyTicketsAsync_PersistsDistinctQrTokenPerTicket()
        {
            var db = _fixture.Db!;
            await SeedCatalogAsync();
            var cliente = await SeedActorAsync("CLIENTE");
            var eventId = $"event-{Guid.NewGuid():N}";
            var ticketTypeId = $"tipo-{Guid.NewGuid():N}";
            await db.Collection("events").Document(eventId).SetAsync(new Event
            {
                Id = eventId,
                Nombre = "Evento",
                FechaInicio = DateTime.UtcNow.AddDays(2),
                FechaFin = DateTime.UtcNow.AddDays(3),
                Estado = Event.EventStatus.Publicado,
                TicketTypes = new List<TicketType> { new() { Id = ticketTypeId, Nombre = "General", Precio = 10, CantidadDisponible = 5, EventoId = eventId } },
            });

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/tickets/buy")
            {
                Content = JsonContent.Create(new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 3 })
            };
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", cliente.Uid);
            var compra = (await (await client.SendAsync(msg)).Content.ReadFromJsonAsync<CompraResponseDto>())!;

            var persisted = new List<Ticket>();
            foreach (var t in compra.Tickets) persisted.Add(await ReadTicketAsync(t.Id));
            Assert.Equal(3, persisted.Select(t => t.QrToken).Distinct().Count());
            Assert.All(persisted, t =>
            {
                Assert.True(QrTokenGenerator.LooksLikeToken(t.QrToken));
                Assert.Equal(1, t.QrVersion);
            });
            Assert.Equal(persisted.Select(t => t.QrToken).OrderBy(x => x), compra.Tickets.Select(t => t.QrToken).OrderBy(x => x));
        }

        // ---- migration command ----

        [FirestoreEmulatorFact]
        public async Task MigrateTicketQrTokens_AssignsTokensToLegacyTickets_LeavesExistingOnesAlone_AndIsIdempotent()
        {
            var db = _fixture.Db!;
            var eventId = await SeedEventAsync();
            var legacyA = await SeedTicketAsync(eventId, "persona-a", qrVersion: 0);
            var legacyB = await SeedTicketAsync(eventId, "persona-b", qrVersion: 0);
            foreach (var legacy in new[] { legacyA, legacyB })
                await db.Collection("tickets").Document(legacy.Id).UpdateAsync(nameof(Ticket.QrToken), FieldValue.Delete);
            var modern = await SeedTicketAsync(eventId, "persona-c", qrVersion: 3);

            var sut = new MigrateTicketQrTokensCommand(db, NullLogger<MigrateTicketQrTokensCommand>.Instance);
            var first = await sut.MigrateAsync();

            Assert.True(first.Migrated >= 2);
            var a = await ReadTicketAsync(legacyA.Id);
            var b = await ReadTicketAsync(legacyB.Id);
            Assert.True(QrTokenGenerator.LooksLikeToken(a.QrToken));
            Assert.True(QrTokenGenerator.LooksLikeToken(b.QrToken));
            Assert.NotEqual(a.QrToken, b.QrToken);
            Assert.Equal(1, a.QrVersion);
            Assert.Equal(1, b.QrVersion);
            var unchanged = await ReadTicketAsync(modern.Id);
            Assert.Equal(modern.QrToken, unchanged.QrToken);
            Assert.Equal(3, unchanged.QrVersion);

            var second = await sut.MigrateAsync();
            Assert.Equal(0, second.Migrated);
            Assert.Equal(a.QrToken, (await ReadTicketAsync(legacyA.Id)).QrToken);
        }

        // ---- email lookup ----

        [FirestoreEmulatorFact]
        public async Task UsuarioRepository_GetByEmail_MatchesTrimmedAndLowerCasedInput_ReturnsNullWhenUnknown()
        {
            var repository = new FirestoreUsuarioRepository(_fixture.Db!);
            var actor = await SeedActorAsync("CLIENTE");

            var exact = await repository.GetByEmailAsync(actor.Email);
            var padded = await repository.GetByEmailAsync($"  {actor.Email.ToUpperInvariant()}  ");
            var unknown = await repository.GetByEmailAsync($"nobody-{Guid.NewGuid():N}@example.com");
            var blank = await repository.GetByEmailAsync("   ");

            Assert.Equal(actor.UsuarioId, exact!.Id);
            Assert.Equal(actor.UsuarioId, padded!.Id);
            Assert.Null(unknown);
            Assert.Null(blank);
        }
    }
}
