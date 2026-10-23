using System;
using System.Security.Cryptography;

namespace HoyDonde.API.Services
{
    // Ticket QR tokens: 128 bits from the OS CSPRNG, URL-safe base64 without padding (22 chars).
    public static class QrTokenGenerator
    {
        public const int ByteLength = 16;
        public const int EncodedLength = 22;

        public static string NewToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(ByteLength);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        // Cheap shape check used to reject garbage before touching Firestore.
        public static bool LooksLikeToken(string? value)
        {
            if (value is null || value.Length != EncodedLength) return false;
            foreach (var c in value)
            {
                var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
