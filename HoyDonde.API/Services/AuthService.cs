using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Flujo Cliente de POST /api/auth/sync (docs/security-refactor-plan.md §2.1, Etapa 3):
    // aprovisiona Persona+Usuario+UsuarioRol(CLIENTE)+IdentidadExterna la primera vez que un uid
    // de Firebase llama a este endpoint, y es idempotente en cualquier llamada siguiente. Nunca
    // crea otro rol, nunca compensa (la identidad de Firebase ya existía antes de llamar acá: la
    // creó el cliente con su propio SDK, no esta API).
    public class AuthService : IAuthService
    {
        private const string RolCliente = "CLIENTE";

        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IPermissionService _permissionService;

        public AuthService(IUsuarioRepository usuarioRepository, IPermissionService permissionService)
        {
            _usuarioRepository = usuarioRepository;
            _permissionService = permissionService;
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

            // Acciones efectivas resueltas por la misma autoridad que autoriza cada request
            // (IPermissionService), nunca desde claims ni desde una tabla hardcodeada de roles.
            // Únicas y en orden determinístico (ordinal ascendente) para que el contrato de
            // /api/auth/sync sea estable entre llamadas.
            var permisos = await _permissionService.GetPermisosEfectivosPorUsuarioIdAsync(result.UsuarioId);
            var acciones = permisos.Acciones
                .Distinct()
                .OrderBy(accion => accion, StringComparer.Ordinal)
                .ToList();

            return new SyncClienteResult(result.UsuarioId, result.PersonaId, roles, acciones);
        }
    }
}
