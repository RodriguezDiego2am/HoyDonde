using Microsoft.AspNetCore.Authorization;

namespace HoyDonde.API.Authorization
{
    // Un requirement por código de Accion (docs/security-refactor-plan.md §3, Etapa 5). Cada
    // policy registrada en Program.cs envuelve exactamente una instancia de este requirement.
    public class AccionRequirement : IAuthorizationRequirement
    {
        public string AccionCodigo { get; }

        public AccionRequirement(string accionCodigo)
        {
            AccionCodigo = accionCodigo;
        }
    }
}
