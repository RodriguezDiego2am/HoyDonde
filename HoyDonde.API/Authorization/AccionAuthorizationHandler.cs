using HoyDonde.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HoyDonde.API.Authorization
{
    // Único punto de autorización por acción (docs/security-refactor-plan.md §3, Etapa 5).
    // Reemplaza a [Authorize(Roles=...)] endpoint por endpoint: cada policy envuelve un
    // AccionRequirement con el código de Accion correspondiente, y este handler resuelve la
    // decisión exclusivamente contra IPermissionService -> Usuario -> UsuarioRol -> Rol ->
    // RolAccion -> Accion (todo activo). El UID sale únicamente de las claims del principal ya
    // autenticado (ClaimTypes.NameIdentifier, "user_id" o "sub", mismo patrón que los
    // controllers); el claim legacy "role" nunca se consulta acá.
    //
    // Cualquier resultado que no sea "autorizado" (identidad ausente, usuario inactivo,
    // asignación inactiva, rol inactivo, acción no asignada, acción inactiva, o un error real de
    // resolución) simplemente no llama a context.Succeed: ASP.NET deniega por default sin que
    // esto se convierta nunca en un 500. Los errores reales de resolución se registran acá
    // (sin exponer detalles sensibles en la decisión de autorización) y no se relanzan.
    public class AccionAuthorizationHandler : AuthorizationHandler<AccionRequirement>
    {
        private const string IdentityProvider = "FIREBASE";

        private readonly IPermissionService _permissionService;
        private readonly ILogger<AccionAuthorizationHandler> _logger;

        public AccionAuthorizationHandler(IPermissionService permissionService, ILogger<AccionAuthorizationHandler> logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AccionRequirement requirement)
        {
            var externalSubjectId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("user_id")?.Value
                ?? context.User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(externalSubjectId))
            {
                return;
            }

            try
            {
                var tieneAccion = await _permissionService.TieneAccionAsync(IdentityProvider, externalSubjectId, requirement.AccionCodigo);
                if (tieneAccion)
                {
                    context.Succeed(requirement);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno resolviendo autorización para la acción {AccionCodigo}", requirement.AccionCodigo);
            }
        }
    }
}
