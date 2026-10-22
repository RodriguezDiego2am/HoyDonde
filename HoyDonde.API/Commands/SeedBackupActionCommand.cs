using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Comando explícito "dotnet run --project HoyDonde.API -- seed-backup-action".
    // No es un endpoint HTTP: se invoca desde Program.cs antes de levantar el servidor y termina
    // el proceso al finalizar, igual que seed-password-reset-action.
    //
    // Pensado para un Firestore real que YA tiene el catálogo de seguridad instalado:
    // SecurityCatalogSeeder.SeedAsync() no vuelve a correr en esas instalaciones. Este comando
    // crea ÚNICAMENTE la Accion BACKUP_ADMINISTRAR -nunca roles, nunca asignaciones Rol->Accion,
    // nunca usuarios-. El Administrador debe asignar esta acción únicamente a ADMINISTRADOR desde
    // /api/security; reejecutar este comando nunca repone una asignación, porque el comando no
    // toca asignaciones en absoluto.
    public class SeedBackupActionCommand
    {
        private const string Codigo = Authorization.Acciones.BackupAdministrar;
        private const string Descripcion = "Crear, restaurar y consultar copias manuales de Firestore.";

        private readonly IAccionRepository _accionRepository;
        private readonly ILogger<SeedBackupActionCommand> _logger;

        public SeedBackupActionCommand(IAccionRepository accionRepository, ILogger<SeedBackupActionCommand> logger)
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
                _logger.LogError(ex, "Error inesperado sembrando la acción BACKUP_ADMINISTRAR.");
                Console.Error.WriteLine("Error inesperado. Revise los logs para más detalle.");
                return 1;
            }
        }
    }
}
