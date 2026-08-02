using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HoyDonde.API.Authentication
{
    // Reemplaza el AddJwtBearer contra Authority=securetoken.google.com (que fallaba con
    // IDX10500 al no resolver las claves públicas) por la verificación recomendada por Firebase
    // para C#: FirebaseAuth.DefaultInstance.VerifyIdTokenAsync, detrás de IFirebaseIdTokenVerifier
    // para poder testear este handler sin el SDK real. Solo autentica (UID); nunca resuelve ni
    // consulta permisos/roles acá -eso sigue siendo responsabilidad exclusiva de
    // AccionAuthorizationHandler/IPermissionService contra Firestore.
    public class FirebaseAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private const string BearerPrefix = "Bearer ";

        private readonly IFirebaseIdTokenVerifier _tokenVerifier;

        public FirebaseAuthenticationHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IFirebaseIdTokenVerifier tokenVerifier)
            : base(options, logger, encoder)
        {
            _tokenVerifier = tokenVerifier;
        }

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authorizationHeader))
            {
                return AuthenticateResult.NoResult();
            }

            var headerValue = authorizationHeader.ToString();
            if (!headerValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return AuthenticateResult.NoResult();
            }

            var idToken = headerValue[BearerPrefix.Length..].Trim();
            if (string.IsNullOrEmpty(idToken))
            {
                return AuthenticateResult.Fail("Falta el ID token en el header Authorization.");
            }

            VerifiedFirebaseToken verified;
            try
            {
                verified = await _tokenVerifier.VerifyIdTokenAsync(idToken);
            }
            catch (Exception)
            {
                // Nunca se registra el token ni el detalle de la excepción del SDK: solo que la
                // verificación falló.
                Logger.LogWarning("Verificación de ID token de Firebase fallida.");
                return AuthenticateResult.Fail("Token de Firebase inválido.");
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, verified.Uid),
                new("user_id", verified.Uid),
                new("sub", verified.Uid),
            };

            if (!string.IsNullOrEmpty(verified.Email))
            {
                claims.Add(new Claim(ClaimTypes.Email, verified.Email));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            return AuthenticateResult.Success(ticket);
        }
    }
}
