using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Text;
using System.Threading.Tasks;

namespace HoyDonde.API.Commands
{
    // Comando explícito "dotnet run --project HoyDonde.API -- bootstrap-admin <email>"
    // (docs/security-refactor-plan.md §5, Etapa 3). No es un endpoint HTTP: se invoca desde
    // Program.cs antes de levantar el servidor y termina el proceso al finalizar. Reutiliza
    // exactamente el mismo aprovisionamiento y compensación que UserService.RegisterAdminAsync
    // usa para las altas de Admin por HTTP.
    public class BootstrapAdminCommand
    {
        public const string BootstrapAssignedByMarker = "BOOTSTRAP";
        private const string RolAdministrador = "ADMINISTRADOR";
        private const string PasswordEnvVar = "HOYDONDE_BOOTSTRAP_ADMIN_PASSWORD";

        private readonly IConfiguration _configuration;
        private readonly SecurityCatalogSeeder _seeder;
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IRolRepository _rolRepository;
        private readonly IUserService _userService;
        private readonly ILogger<BootstrapAdminCommand> _logger;

        public BootstrapAdminCommand(
            IConfiguration configuration,
            SecurityCatalogSeeder seeder,
            IUsuarioRepository usuarioRepository,
            IRolRepository rolRepository,
            IUserService userService,
            ILogger<BootstrapAdminCommand> logger)
        {
            _configuration = configuration;
            _seeder = seeder;
            _usuarioRepository = usuarioRepository;
            _rolRepository = rolRepository;
            _userService = userService;
            _logger = logger;
        }

        // passwordReader es un punto de extensión para pruebas: evita depender de Console real.
        // En uso normal (CLI real) queda null y se usa entrada oculta por consola.
        public async Task<int> RunAsync(string[] args, Func<string?>? passwordReader = null)
        {
            if (!_configuration.GetValue<bool>("Bootstrap:AllowAdminBootstrap"))
            {
                Console.Error.WriteLine("Bootstrap de Administrador deshabilitado. Configure Bootstrap:AllowAdminBootstrap=true (o la variable de entorno equivalente) para habilitarlo.");
                return 1;
            }

            var email = args.Length > 1 ? args[1] : null;
            if (string.IsNullOrWhiteSpace(email))
            {
                Console.Error.WriteLine("Uso: dotnet run --project HoyDonde.API -- bootstrap-admin <email>");
                return 1;
            }

            // Idempotente: sembrar antes de verificar/crear garantiza que el Rol ADMINISTRADOR
            // exista para la verificación de abajo, sin importar cuántas veces se corrió antes.
            await _seeder.SeedAsync();

            if (await ExisteAdministradorEfectivoAsync())
            {
                Console.Error.WriteLine("Ya existe un Administrador efectivo. Bootstrap abortado: no se creó ni modificó nada.");
                return 1;
            }

            var password = Environment.GetEnvironmentVariable(PasswordEnvVar);
            if (string.IsNullOrEmpty(password))
            {
                password = passwordReader != null ? passwordReader() : ReadPasswordFromConsoleIfInteractive();
            }

            if (string.IsNullOrEmpty(password))
            {
                Console.Error.WriteLine($"No se proveyó contraseña. Configure la variable de entorno {PasswordEnvVar} o corra el comando de forma interactiva.");
                return 1;
            }

            await _userService.RegisterAdminAsync(BootstrapAssignedByMarker, email, password);
            Console.WriteLine($"Administrador '{email}' aprovisionado correctamente.");
            return 0;
        }

        // "Administrador efectivo": un Usuario activo con una asignación activa de
        // UsuarioRol(ADMINISTRADOR) sobre un Rol ADMINISTRADOR también activo.
        private async Task<bool> ExisteAdministradorEfectivoAsync()
        {
            var rolAdministrador = await _rolRepository.GetByCodigoAsync(RolAdministrador);
            if (rolAdministrador == null || !rolAdministrador.Activo) return false;

            var usuarioIds = await _usuarioRepository.GetUsuarioIdsConRolActivoAsync(RolAdministrador);
            foreach (var usuarioId in usuarioIds)
            {
                var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
                if (usuario != null && usuario.IsActive)
                {
                    return true;
                }
            }

            return false;
        }

        private static string? ReadPasswordFromConsoleIfInteractive()
        {
            if (Console.IsInputRedirected)
            {
                // No hay TTY (CI, pipe, emulador de pruebas): no hay forma segura de pedir
                // entrada oculta. El llamador reporta el error de contraseña faltante.
                return null;
            }

            Console.Write("Contraseña del Administrador: ");
            var password = new StringBuilder();
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                }
            } while (key.Key != ConsoleKey.Enter);
            Console.WriteLine();

            return password.ToString();
        }
    }
}
