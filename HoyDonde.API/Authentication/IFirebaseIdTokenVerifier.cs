using System.Threading.Tasks;

namespace HoyDonde.API.Authentication
{
    // Resultado mínimo de una verificación exitosa: solo lo que la API necesita para construir
    // claims (UID, email opcional). Nunca lleva el token ni el resto de los claims del SDK.
    public record VerifiedFirebaseToken(string Uid, string? Email);

    // Pequeña abstracción sobre FirebaseAuth.DefaultInstance.VerifyIdTokenAsync
    // (FirebaseIdTokenVerifier, implementación real) para que FirebaseAuthenticationHandler sea
    // testeable sin depender del singleton estático de Firebase Admin SDK.
    public interface IFirebaseIdTokenVerifier
    {
        Task<VerifiedFirebaseToken> VerifyIdTokenAsync(string idToken);
    }
}
