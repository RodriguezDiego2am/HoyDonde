using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Comando explícito "dotnet run --project HoyDonde.API -- seed-role-deletion-action"
    // (docs/api-mvp-plan.md §12). No es un endpoint HTTP: se invoca desde Program.cs antes de
    // levantar el servidor y termina el proceso al finalizar, igual que seed-report-actions.
    //
    // Pensado para un Firestore real que YA tiene el catálogo de seguridad instalado:
    // SecurityCatalogSeeder.SeedAsync() no vuelve a correr en esas instalaciones. Este comando
    // crea ÚNICAMENTE la Accion ROL_ELIMINAR -nunca roles, nunca asignaciones Rol->Accion, nunca
    // usuarios-. El Administrador asigna esa acción a los roles que decida desde /admin/roles;
    // reejecutar este comando nunca repone una asignación que el Administrador haya quitado,
    // porque el comando no toca asignaciones en absoluto.
    public class SeedRoleDeletionActionCommand
    {
        private const string Codigo = Authorization.Acciones.RolEliminar;
        private const string Descripcion = "Eliminar físicamente un rol personalizado inactivo sin usuarios asignados.";

        private readonly IAccionRepository _accionRepository;
        private readonly ILogger<SeedRoleDeletionActionCommand> _logger;

        public SeedRoleDeletionActionCommand(IAccionRepository accionRepository, ILogger<SeedRoleDeletionActionCommand> logger)
        {
            _accionRepository = accionRepository;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            try
            {
                await _accionRepository.CreateAsync(new Accion { Codigo = Codigo, Descripcion = Descripcion });
                Console.WriteLine($"Acción '{Codigo}': creada.");
                return 0;
            }
            catch (AccionYaExisteException)
            {
                Console.WriteLine($"Acción '{Codigo}': ya existente.");
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado sembrando la acción ROL_ELIMINAR.");
                Console.Error.WriteLine("Error inesperado. Revise los logs para más detalle.");
                return 1;
            }
        }
    }
}
