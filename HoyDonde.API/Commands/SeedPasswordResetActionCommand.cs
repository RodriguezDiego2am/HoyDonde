using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Comando explícito "dotnet run --project HoyDonde.API -- seed-password-reset-action"
    // (docs/api-mvp-plan.md §13). No es un endpoint HTTP: se invoca desde Program.cs antes de
    // levantar el servidor y termina el proceso al finalizar, igual que seed-report-actions y
    // seed-role-deletion-action.
    //
    // Pensado para un Firestore real que YA tiene el catálogo de seguridad instalado:
    // SecurityCatalogSeeder.SeedAsync() no vuelve a correr en esas instalaciones. Este comando
    // crea ÚNICAMENTE la Accion USUARIO_RESTABLECER_PASSWORD -nunca roles, nunca asignaciones
    // Rol->Accion, nunca usuarios-. El Administrador asigna esa acción a los roles que decida
    // desde /admin/roles; reejecutar este comando nunca repone una asignación que el
    // Administrador haya quitado, porque el comando no toca asignaciones en absoluto.
    public class SeedPasswordResetActionCommand
    {
        private const string Codigo = Authorization.Acciones.UsuarioRestablecerPassword;
        private const string Descripcion = "Generar un enlace de recuperación de contraseña para otro usuario.";

        private readonly IAccionRepository _accionRepository;
        private readonly ILogger<SeedPasswordResetActionCommand> _logger;

        public SeedPasswordResetActionCommand(IAccionRepository accionRepository, ILogger<SeedPasswordResetActionCommand> logger)
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
                _logger.LogError(ex, "Error inesperado sembrando la acción USUARIO_RESTABLECER_PASSWORD.");
                Console.Error.WriteLine("Error inesperado. Revise los logs para más detalle.");
                return 1;
            }
        }
    }
}
