using System;
using System.Linq;
using System.Threading.Tasks;
using HoyDonde.API.Authorization;
using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Services
{
    // Free, immediate ticket transfer (resale phase 1). Orchestrates identity resolution and
    // recipient lookup; every invariant that must hold atomically (ownership, state, event state,
    // rate limit, token rotation, audit) lives in ITicketTransferStore's single transaction.
    public class TicketTransferService : ITicketTransferService
    {
        public const int MaxTransfersPerWindow = 10;
        // Failed recipient lookups per sender per window (separate budget from successful transfers:
        // a successful lookup costs the sender a ticket, a failed one costs nothing, so only the
        // failures need their own cap against email enumeration).
        public const int MaxFailedLookupsPerWindow = 10;
        public static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IPermissionService _permissionService;
        private readonly ITicketTransferStore _store;
        private readonly ILogger<TicketTransferService> _logger;

        public TicketTransferService(
            IAuthenticatedPersonaResolver personaResolver,
            IUsuarioRepository usuarioRepository,
            IPermissionService permissionService,
            ITicketTransferStore store,
            ILogger<TicketTransferService> logger)
        {
            _personaResolver = personaResolver;
            _usuarioRepository = usuarioRepository;
            _permissionService = permissionService;
            _store = store;
            _logger = logger;
        }

        public async Task<TicketTransferResponseDto> TransferAsync(string actorUid, string ticketId, string recipientEmail)
        {
            var fromPersonaId = await _personaResolver.ResolvePersonaIdAsync(actorUid);

            // Ownership/state and the failed-lookup budget come BEFORE the recipient lookup: the
            // lookup answers "is this email registered?", so it must never be reachable without an
            // owned, transferable ticket. The authoritative checks are repeated in TryTransferAsync.
            var precheck = await _store.PrecheckAndReserveLookupAsync(new TicketTransferPrecheckRequest(
                ticketId, fromPersonaId, MaxFailedLookupsPerWindow, RateLimitWindow));
            switch (precheck.Status)
            {
                case TicketTransferStatus.Success:
                    break;
                case TicketTransferStatus.NotTransferable:
                    throw new TicketNoTransferibleException();
                case TicketTransferStatus.RateLimited:
                    _logger.LogWarning("Persona {FromPersonaId} alcanzó el límite de búsquedas de destinatario fallidas.", fromPersonaId);
                    throw new TransferenciaLimiteExcedidoException();
                default:
                    throw new TicketNoEncontradoException();
            }

            // A failed lookup keeps its reservation (it is the failure record); a lookup that
            // resolves a valid recipient releases it below.
            var toPersonaId = await ResolveRecipientPersonaIdAsync(recipientEmail);
            await ReleaseLookupAsync(precheck.LookupReservationId, fromPersonaId);
            if (string.Equals(toPersonaId, fromPersonaId, StringComparison.Ordinal))
                throw new TransferenciaAUnoMismoException();

            var result = await _store.TryTransferAsync(new TicketTransferRequest(
                ticketId, fromPersonaId, toPersonaId, QrTokenGenerator.NewToken(), MaxTransfersPerWindow, RateLimitWindow));

            switch (result.Status)
            {
                case TicketTransferStatus.Success:
                    // Persona ids and ticket id only: never the email or any QR token.
                    _logger.LogInformation(
                        "Ticket {TicketId} transferido de persona {FromPersonaId} a persona {ToPersonaId} (QrVersion {QrVersion}).",
                        ticketId, fromPersonaId, toPersonaId, result.NewQrVersion);
                    return new TicketTransferResponseDto
                    {
                        TicketId = ticketId,
                        TransferredAt = result.TransferredAtUtc ?? DateTime.UtcNow,
                    };
                case TicketTransferStatus.NotTransferable:
                    throw new TicketNoTransferibleException();
                case TicketTransferStatus.RateLimited:
                    _logger.LogWarning("Persona {FromPersonaId} alcanzó el límite de transferencias.", fromPersonaId);
                    throw new TransferenciaLimiteExcedidoException();
                default:
                    throw new TicketNoEncontradoException();
            }
        }

        private async Task ReleaseLookupAsync(string? reservationId, string fromPersonaId)
        {
            if (reservationId is null) return;
            try
            {
                await _store.ReleaseLookupAsync(reservationId);
            }
            catch (Exception ex)
            {
                // Best effort: an unreleased reservation only errs on the side of counting.
                _logger.LogWarning(ex, "No se pudo liberar la reserva de búsqueda de la persona {FromPersonaId}.", fromPersonaId);
            }
        }

        // Unknown email, inactive account and an account that cannot hold tickets all collapse
        // into one exception whose public message never reveals which case applied.
        private async Task<string> ResolveRecipientPersonaIdAsync(string recipientEmail)
        {
            var usuario = await _usuarioRepository.GetByEmailAsync(recipientEmail);
            if (usuario is null || !usuario.IsActive || string.IsNullOrEmpty(usuario.PersonaId))
                throw new TransferenciaDestinatarioInvalidoException();

            var permisos = await _permissionService.GetPermisosEfectivosPorUsuarioIdAsync(usuario.Id);
            if (!permisos.UsuarioActivo || !permisos.Acciones.Contains(Acciones.TicketVerPropio))
                throw new TransferenciaDestinatarioInvalidoException();

            return usuario.PersonaId;
        }
    }
}
