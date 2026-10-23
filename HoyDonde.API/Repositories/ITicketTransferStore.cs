using System;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public enum TicketTransferStatus
    {
        Success,
        // Missing ticket or not owned by the sender (indistinguishable on purpose).
        NotFound,
        // Used, annulled, or event cancelled / finished.
        NotTransferable,
        RateLimited
    }

    public record TicketTransferRequest(
        string TicketId,
        string FromPersonaId,
        string ToPersonaId,
        string NewQrToken,
        int MaxTransfersPerWindow,
        TimeSpan RateLimitWindow);

    // Read-only-ish pre-check run BEFORE the recipient email is resolved. It requires the ticket to
    // exist, belong to the sender and be transferable, then atomically reserves one recipient-lookup
    // attempt against the sender's hourly limit of failed lookups (no email is ever stored).
    public record TicketTransferPrecheckRequest(
        string TicketId,
        string FromPersonaId,
        int MaxFailedLookupsPerWindow,
        TimeSpan Window);

    // Status is Success when the lookup may proceed (LookupReservationId is then set); NotFound /
    // NotTransferable mirror TryTransferAsync; RateLimited means the failed-lookup limit is reached.
    public record TicketTransferPrecheckResult(TicketTransferStatus Status, string? LookupReservationId = null);

    public record TicketTransferResult(TicketTransferStatus Status, DateTime? TransferredAtUtc = null, int NewQrVersion = 0);

    // Atomic ownership transfer behind an interface so TicketTransferService can be unit-tested
    // with an in-memory fake. Implementations must, in ONE transaction: read the ticket and its
    // event, check ownership / state / effective event state / the sender's rate limit, then write
    // the new owner, the rotated QR token and the audit row. Nothing is written otherwise.
    //
    // The pre-check + lookup reservation is NOT a substitute for TryTransferAsync's checks: it only
    // makes sure the recipient lookup (an email enumeration oracle) is never reachable without an
    // owned, transferable ticket and within the sender's failed-lookup budget. A reservation counts
    // as a failed lookup until it is released, so a crash or error between reserve and release errs
    // on the side of counting it.
    public interface ITicketTransferStore
    {
        Task<TicketTransferPrecheckResult> PrecheckAndReserveLookupAsync(TicketTransferPrecheckRequest request);

        // Frees a reservation whose lookup did not fail (valid recipient, or transfer to oneself).
        Task ReleaseLookupAsync(string lookupReservationId);

        Task<TicketTransferResult> TryTransferAsync(TicketTransferRequest request);
    }
}
