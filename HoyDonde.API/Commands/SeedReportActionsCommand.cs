using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;

namespace HoyDonde.API.Commands
{
    // Comando explícito "dotnet run --project HoyDonde.API -- seed-report-actions"
    // (docs/api-mvp-plan.md §11.5). No es un endpoint HTTP: se invoca desde Program.cs antes de
    // levantar el servidor y termina el proceso al finalizar, igual que bootstrap-admin.
    //
    // Pensado para un Firestore real que YA tiene el catálogo de seguridad instalado:
    // SecurityCatalogSeeder.SeedAsync() no vuelve a correr en esas instalaciones (solo se invoca
    // desde bootstrap-admin, que se niega una vez que existe un Administrador efectivo). Este
    // comando crea ÚNICAMENTE las dos Acciones nuevas del módulo de reportes -nunca roles, nunca
    // asignaciones Rol->Accion, nunca usuarios-. El Administrador asigna esas acciones a los roles
    // que decida desde /admin/roles; reejecutar este comando nunca repone una asignación que el
    // Administrador haya quitado, porque el comando no toca asignaciones en absoluto.
    public class SeedReportActionsCommand
    {
        private static readonly (string Codigo, string Descripcion)[] AccionesReporte =
        {
            (Authorization.Acciones.ReporteVerGlobal, "Consultar el reporte global de eventos de todos los organizadores."),
            (Authorization.Acciones.ReporteVerPropio, "Consultar el reporte de los propios eventos."),
        };

        private readonly IAccionRepository _accionRepository;
        private readonly ILogger<SeedReportActionsCommand> _logger;

        public SeedReportActionsCommand(IAccionRepository accionRepository, ILogger<SeedReportActionsCommand> logger)
        {
            _accionRepository = accionRepository;
            _logger = logger;
        }

        public async Task<int> RunAsync()
        {
            try
            {
                foreach (var (codigo, descripcion) in AccionesReporte)
                {
                    await CrearSiNoExisteAsync(codigo, descripcion);
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado sembrando las acciones del módulo de reportes.");
                Console.Error.WriteLine("Error inesperado. Revise los logs para más detalle. No se garantiza qué acciones quedaron creadas.");
                return 1;
            }
        }

        private async Task CrearSiNoExisteAsync(string codigo, string descripcion)
        {
            try
            {
                await _accionRepository.CreateAsync(new Accion { Codigo = codigo, Descripcion = descripcion });
                Console.WriteLine($"Acción '{codigo}': creada.");
            }
            catch (AccionYaExisteException)
            {
                Console.WriteLine($"Acción '{codigo}': ya existente.");
            }
        }
    }
}
