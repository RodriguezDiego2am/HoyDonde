using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Flujo Cliente de POST /api/auth/sync (docs/security-refactor-plan.md §2.1, Etapa 3):
    // aprovisiona Persona+Usuario+UsuarioRol(CLIENTE)+IdentidadExterna la primera vez que un uid
    // de Firebase llama a este endpoint, y es idempotente en cualquier llamada siguiente. Nunca
    // crea otro rol, nunca compensa (la identidad de Firebase ya existía antes de llamar acá: la
    // creó el cliente con su propio SDK, no esta API), y nunca escribe en users/user_audits
    // legacy.
    public class AuthService : IAuthService
    {
        private const string RolCliente = "CLIENTE";

        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IIdentityProvider _identityProvider;

        public AuthService(IUsuarioRepository usuarioRepository, IIdentityProvider identityProvider)
        {
            _usuarioRepository = usuarioRepository;
            _identityProvider = identityProvider;
        }

        public async Task<SyncClienteResult> SyncClienteAsync(string uid, string email, SyncClienteRequest request)
        {
            // PersonaId/UsuarioId se generan siempre, incluso si terminan sin usarse: la
            // idempotencia real la garantiza la transacción de ProvisionarAsync (Etapa 2), que
            // detecta la IdentidadExterna ya existente y devuelve los IDs originales sin escribir
            // ni duplicar nada. Esto es lo que hace que un Admin/Organizador/Control que llame acá
            // nunca se convierta en Cliente: su Usuario ya existe, así que ProvisionarAsync no
            // toca su colección de roles.
            var personaId = Guid.NewGuid().ToString();
            var usuarioId = Guid.NewGuid().ToString();

            var provisioningRequest = new UsuarioProvisioningRequest(
                personaId, usuarioId, FirebaseIdentityProvider.ProviderName, uid,
                email, RolCliente, UserService.SelfRegistrationActor,
                FullName: request.FullName, Dni: request.Dni, PhoneNumber: request.PhoneNumber);

            var result = await _usuarioRepository.ProvisionarAsync(provisioningRequest);
            var roles = await _usuarioRepository.GetRolCodigosActivosAsync(result.UsuarioId);

            // El claim legacy "role" solo tiene sentido -y solo se toca- si el rol real del
            // Usuario es CLIENTE. Compatibilidad de código transitoria mientras existan endpoints
            // con [Authorize(Roles = Roles.Cliente)] (§2.1 punto 7 / §3, se retira en la Etapa 6).
            var claimsUpdated = false;
            if (roles.Contains(RolCliente))
            {
                var claims = new Dictionary<string, object> { { "role", Roles.Cliente } };
                await _identityProvider.SetTemporaryClaimAsync(uid, claims);
                claimsUpdated = true;
            }

            return new SyncClienteResult(result.UsuarioId, result.PersonaId, roles, claimsUpdated);
        }
    }
}
