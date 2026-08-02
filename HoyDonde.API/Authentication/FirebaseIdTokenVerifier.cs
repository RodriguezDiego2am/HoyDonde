using System.Threading.Tasks;
using FirebaseAdmin.Auth;

namespace HoyDonde.API.Authentication
{
    // Implementación real de IFirebaseIdTokenVerifier: delega en el Firebase Admin SDK
    // (FirebaseAuth.DefaultInstance.VerifyIdTokenAsync), que descarga y cachea las claves
    // públicas de Google internamente. No se implementa acá ningún manejo manual de
    // certificados/caché.
    public class FirebaseIdTokenVerifier : IFirebaseIdTokenVerifier
    {
        public async Task<VerifiedFirebaseToken> VerifyIdTokenAsync(string idToken)
        {
            var decoded = await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);

            string? email = null;
            if (decoded.Claims.TryGetValue("email", out var emailClaim))
            {
                email = emailClaim?.ToString();
            }

            return new VerifiedFirebaseToken(decoded.Uid, email);
        }
    }
}
