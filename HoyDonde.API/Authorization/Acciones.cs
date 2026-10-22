using System.Collections.Generic;

namespace HoyDonde.API.Authorization
{
    // Central catalog for policies and SecurityCatalogSeeder. New entries must be added to both.
    public static class Acciones
    {
        public const string UsuarioCrearAdmin = "USUARIO_CREAR_ADMIN";
        public const string UsuarioCrearOrganizador = "USUARIO_CREAR_ORGANIZADOR";
        public const string ControlCrear = "CONTROL_CREAR";
        public const string EventoCrear = "EVENTO_CREAR";
        public const string EventoEditarPropio = "EVENTO_EDITAR_PROPIO";
        public const string EventoPublicarPropio = "EVENTO_PUBLICAR_PROPIO";
        public const string EventoCancelarPropio = "EVENTO_CANCELAR_PROPIO";
        public const string EventoVerPropios = "EVENTO_VER_PROPIOS";
        public const string TicketComprar = "TICKET_COMPRAR";
        public const string TicketVerPropio = "TICKET_VER_PROPIO";
        public const string TicketValidar = "TICKET_VALIDAR";
        public const string RolCrear = "ROL_CREAR";
        public const string RolEditar = "ROL_EDITAR";
        public const string RolActivar = "ROL_ACTIVAR";
        public const string RolAsignarAccion = "ROL_ASIGNAR_ACCION";
        public const string RolQuitarAccion = "ROL_QUITAR_ACCION";
        public const string RolEliminar = "ROL_ELIMINAR";
        public const string UsuarioAsignarRol = "USUARIO_ASIGNAR_ROL";
        public const string UsuarioQuitarRol = "USUARIO_QUITAR_ROL";
        public const string UsuarioVerPermisosEfectivos = "USUARIO_VER_PERMISOS_EFECTIVOS";
        public const string UsuarioDesactivar = "USUARIO_DESACTIVAR";
        public const string ReporteVerGlobal = "REPORTE_VER_GLOBAL";
        public const string ReporteVerPropio = "REPORTE_VER_PROPIO";
        public const string UsuarioRestablecerPassword = "USUARIO_RESTABLECER_PASSWORD";
        public const string BackupAdministrar = "BACKUP_ADMINISTRAR";

        public static readonly IReadOnlyList<string> Todas = new[]
        {
            UsuarioCrearAdmin, UsuarioCrearOrganizador, ControlCrear,
            EventoCrear, EventoEditarPropio, EventoPublicarPropio, EventoCancelarPropio, EventoVerPropios,
            TicketComprar, TicketVerPropio, TicketValidar,
            RolCrear, RolEditar, RolActivar, RolAsignarAccion, RolQuitarAccion, RolEliminar,
            UsuarioAsignarRol, UsuarioQuitarRol, UsuarioVerPermisosEfectivos, UsuarioDesactivar,
            ReporteVerGlobal, ReporteVerPropio, UsuarioRestablecerPassword, BackupAdministrar,
        };
    }
}
