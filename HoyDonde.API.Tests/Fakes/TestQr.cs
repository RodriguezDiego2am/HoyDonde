using HoyDonde.API.Services;

namespace HoyDonde.API.Tests.Fakes
{
    // Deterministic, well-formed (22 url-safe chars) QR tokens for tests that need to name them.
    public static class TestQr
    {
        public static string Token(string seed)
        {
            var clean = new string(System.Linq.Enumerable.ToArray(
                System.Linq.Enumerable.Select(seed, c => char.IsLetterOrDigit(c) ? c : '_')));
            var padded = ("T" + clean).PadRight(QrTokenGenerator.EncodedLength, 'x');
            return padded.Substring(0, QrTokenGenerator.EncodedLength);
        }
    }
}
