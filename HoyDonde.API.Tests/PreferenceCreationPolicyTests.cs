using HoyDonde.API.Services;
using Xunit;

namespace HoyDonde.API.Tests
{
    public class PreferenceCreationPolicyTests
    {
        [Fact]
        public void Resolve_AmbiguousProviderOutcomeIsCreationUnknownAndNeverRetries()
        {
            var outcome = PreferenceCreationPolicy.Resolve(PreferenceCreationOutcome.AmbiguousFailure);

            Assert.Equal(CheckoutCreationState.CreationUnknown, outcome.State);
            Assert.False(outcome.MayRetryAutomatically);
        }

        [Fact]
        public void Resolve_KnownProviderFailureDoesNotPretendCheckoutIsReady()
        {
            var outcome = PreferenceCreationPolicy.Resolve(PreferenceCreationOutcome.KnownFailure);

            Assert.Equal(CheckoutCreationState.Failed, outcome.State);
            Assert.False(outcome.MayRetryAutomatically);
        }
    }
}
