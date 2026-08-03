using HoyDonde.API.Authorization;
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
            (Acciones.UsuarioCrearAdmin, "Crear una cuenta de Administrador."),
            (Acciones.UsuarioCrearOrganizador, "Crear una cuenta de Organizador."),
            (Acciones.ControlCrear, "Crear personal de Control para un evento propio."),
            (Acciones.EventoCrear, "Crear un evento."),
            (Acciones.EventoEditarPropio, "Editar un evento propio."),
            (Acciones.EventoPublicarPropio, "Publicar un evento propio."),
            (Acciones.EventoCancelarPropio, "Cancelar un evento propio."),
            (Acciones.EventoVerPropios, "Consultar los eventos propios."),
            (Acciones.TicketComprar, "Comprar entradas."),
            (Acciones.TicketVerPropio, "Consultar las entradas propias."),
            (Acciones.TicketValidar, "Validar una entrada en el acceso a un evento."),
            // Administración de roles y acciones
            (Acciones.RolCrear, "Crear un rol."),
            (Acciones.RolEditar, "Editar nombre/descripción de un rol."),
            (Acciones.RolActivar, "Activar o desactivar un rol."),
            (Acciones.RolAsignarAccion, "Asignar una acción a un rol."),
            (Acciones.RolQuitarAccion, "Quitar una acción de un rol."),
            (Acciones.UsuarioAsignarRol, "Asignar un rol a un usuario."),
            (Acciones.UsuarioQuitarRol, "Quitar un rol de un usuario."),
            (Acciones.UsuarioVerPermisosEfectivos, "Consultar los permisos efectivos de un usuario."),
            (Acciones.UsuarioDesactivar, "Activar o desactivar un usuario."),
            // Módulo de reportes (docs/api-mvp-plan.md §11)
            (Acciones.ReporteVerGlobal, "Consultar el reporte global de eventos de todos los organizadores."),
            (Acciones.ReporteVerPropio, "Consultar el reporte de los propios eventos."),
        };

        private static readonly IReadOnlyDictionary<string, string[]> AccionesPorRol = new Dictionary<string, string[]>
        {
            ["ADMINISTRADOR"] = new[]
            {
                Acciones.UsuarioCrearAdmin, Acciones.UsuarioCrearOrganizador,
                Acciones.RolCrear, Acciones.RolEditar, Acciones.RolActivar, Acciones.RolAsignarAccion, Acciones.RolQuitarAccion,
                Acciones.UsuarioAsignarRol, Acciones.UsuarioQuitarRol, Acciones.UsuarioVerPermisosEfectivos, Acciones.UsuarioDesactivar,
                Acciones.ReporteVerGlobal,
            },
            ["ORGANIZADOR"] = new[]
            {
                Acciones.ControlCrear, Acciones.EventoCrear, Acciones.EventoEditarPropio,
                Acciones.EventoPublicarPropio, Acciones.EventoCancelarPropio, Acciones.EventoVerPropios,
                Acciones.ReporteVerPropio,
            },
            ["CLIENTE"] = new[] { Acciones.TicketComprar, Acciones.TicketVerPropio },
            ["CONTROL"] = new[] { Acciones.TicketValidar },
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
