using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Siembra el catálogo inicial de Rol/Accion de la Etapa 1 del refactor de seguridad
    // (docs/security-refactor-plan.md §14/§16). Idempotente: correr SeedAsync varias veces
    // no falla ni duplica nada.
    //
    // El mapeo Rol->Accion refleja la paridad de autorización ya vigente hoy en el código
    // legacy ([Authorize(Roles=...)] por endpoint, ver UserController/EventsController/
    // TicketsController) más las acciones de administración de roles/acciones (§7), que
    // quedan reservadas a ADMINISTRADOR.
    public class SecurityCatalogSeeder
    {
        public const string SeedActor = "seed:security-catalog";

        private static readonly (string Codigo, string Nombre, string Descripcion)[] RolesIniciales =
        {
            ("ADMINISTRADOR", "Administrador", "Administra roles, acciones y usuarios del sistema."),
            ("CLIENTE", "Cliente", "Compra y consulta sus propias entradas."),
            ("ORGANIZADOR", "Organizador", "Crea y gestiona sus propios eventos."),
            ("CONTROL", "Control", "Valida entradas en el acceso a eventos asignados."),
        };

        private static readonly (string Codigo, string Descripcion)[] AccionesIniciales =
        {
            // Paridad con endpoints existentes
            ("USUARIO_CREAR_ADMIN", "Crear una cuenta de Administrador."),
            ("USUARIO_CREAR_ORGANIZADOR", "Crear una cuenta de Organizador."),
            ("CONTROL_CREAR", "Crear personal de Control para un evento propio."),
            ("EVENTO_CREAR", "Crear un evento."),
            ("EVENTO_EDITAR_PROPIO", "Editar un evento propio."),
            ("EVENTO_PUBLICAR_PROPIO", "Publicar un evento propio."),
            ("EVENTO_CANCELAR_PROPIO", "Cancelar un evento propio."),
            ("EVENTO_VER_PROPIOS", "Consultar los eventos propios."),
            ("TICKET_COMPRAR", "Comprar entradas."),
            ("TICKET_VER_PROPIO", "Consultar las entradas propias."),
            ("TICKET_VALIDAR", "Validar una entrada en el acceso a un evento."),
            // Administración de roles y acciones
            ("ROL_CREAR", "Crear un rol."),
            ("ROL_EDITAR", "Editar nombre/descripción de un rol."),
            ("ROL_ACTIVAR", "Activar o desactivar un rol."),
            ("ROL_ASIGNAR_ACCION", "Asignar una acción a un rol."),
            ("ROL_QUITAR_ACCION", "Quitar una acción de un rol."),
            ("USUARIO_ASIGNAR_ROL", "Asignar un rol a un usuario."),
            ("USUARIO_QUITAR_ROL", "Quitar un rol de un usuario."),
            ("USUARIO_VER_PERMISOS_EFECTIVOS", "Consultar los permisos efectivos de un usuario."),
            ("USUARIO_DESACTIVAR", "Activar o desactivar un usuario."),
        };

        private static readonly IReadOnlyDictionary<string, string[]> AccionesPorRol = new Dictionary<string, string[]>
        {
            ["ADMINISTRADOR"] = new[]
            {
                "USUARIO_CREAR_ADMIN", "USUARIO_CREAR_ORGANIZADOR",
                "ROL_CREAR", "ROL_EDITAR", "ROL_ACTIVAR", "ROL_ASIGNAR_ACCION", "ROL_QUITAR_ACCION",
                "USUARIO_ASIGNAR_ROL", "USUARIO_QUITAR_ROL", "USUARIO_VER_PERMISOS_EFECTIVOS", "USUARIO_DESACTIVAR",
            },
            ["ORGANIZADOR"] = new[]
            {
                "CONTROL_CREAR", "EVENTO_CREAR", "EVENTO_EDITAR_PROPIO",
                "EVENTO_PUBLICAR_PROPIO", "EVENTO_CANCELAR_PROPIO", "EVENTO_VER_PROPIOS",
            },
            ["CLIENTE"] = new[] { "TICKET_COMPRAR", "TICKET_VER_PROPIO" },
            ["CONTROL"] = new[] { "TICKET_VALIDAR" },
        };

        private readonly IRolRepository _rolRepository;
        private readonly IAccionRepository _accionRepository;

        public SecurityCatalogSeeder(IRolRepository rolRepository, IAccionRepository accionRepository)
        {
            _rolRepository = rolRepository;
            _accionRepository = accionRepository;
        }

        public async Task SeedAsync()
        {
            foreach (var (codigo, nombre, descripcion) in RolesIniciales)
            {
                await CreateRolIfMissingAsync(codigo, nombre, descripcion);
            }

            foreach (var (codigo, descripcion) in AccionesIniciales)
            {
                await CreateAccionIfMissingAsync(codigo, descripcion);
            }

            foreach (var (rolCodigo, accionCodigos) in AccionesPorRol)
            {
                foreach (var accionCodigo in accionCodigos)
                {
                    await _rolRepository.AssignAccionAsync(rolCodigo, accionCodigo, SeedActor);
                }
            }
        }

        private async Task CreateRolIfMissingAsync(string codigo, string nombre, string descripcion)
        {
            try
            {
                await _rolRepository.CreateAsync(new Rol { Codigo = codigo, Nombre = nombre, Descripcion = descripcion });
            }
            catch (RolYaExisteException)
            {
                // Ya sembrado en una corrida anterior: la siembra es idempotente por diseño.
            }
        }

        private async Task CreateAccionIfMissingAsync(string codigo, string descripcion)
        {
            try
            {
                await _accionRepository.CreateAsync(new Accion { Codigo = codigo, Descripcion = descripcion });
            }
            catch (AccionYaExisteException)
            {
                // Ya sembrada en una corrida anterior: la siembra es idempotente por diseño.
            }
        }
    }
}
