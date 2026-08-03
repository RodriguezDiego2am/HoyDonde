using System.Collections.Generic;

namespace HoyDonde.API.Repositories
{
    // Los 4 roles base sembrados por SecurityCatalogSeeder (docs/api-mvp-plan.md §12): nunca
    // pueden eliminarse físicamente, sin importar su estado o sus asignaciones.
    internal static class RolesEsenciales
    {
        public static readonly IReadOnlyCollection<string> Codigos = new HashSet<string>
        {
            "ADMINISTRADOR", "ORGANIZADOR", "CLIENTE", "CONTROL",
        };

        public static bool EsEsencial(string codigo) => Codigos.Contains(codigo);
    }
}
