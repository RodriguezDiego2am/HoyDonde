namespace HoyDonde.API.Services
{
    public enum PreferenceCreationOutcome { Created, KnownFailure, AmbiguousFailure }
    public enum CheckoutCreationState { Ready, Failed, CreationUnknown }
    public sealed record PreferenceCreationDecision(CheckoutCreationState State, bool MayRetryAutomatically);

    // Checkout preference creation has no validated idempotency contract. In particular, timeouts
    // and connection loss are ambiguous: keep the local reservation observable but never issue a
    // second provider create request automatically.
    public static class PreferenceCreationPolicy
    {
        public static PreferenceCreationDecision Resolve(PreferenceCreationOutcome outcome) => outcome switch
        {
            PreferenceCreationOutcome.Created => new(CheckoutCreationState.Ready, false),
            PreferenceCreationOutcome.AmbiguousFailure => new(CheckoutCreationState.CreationUnknown, false),
            _ => new(CheckoutCreationState.Failed, false),
        };
    }
}
