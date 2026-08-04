using HoyDonde.API.DTOs;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Administración configurable de roles/acciones asignadas/usuarios
    // (docs/security-refactor-plan.md §6, Etapa 5). Compone IRolRepository/IUsuarioRepository
    // (cada mutación transaccional con su SecurityAudit, ver esos repos) más la resolución de
    // permisos efectivos vía IPermissionService. El actor de cada operación se resuelve siempre
    // desde el externalSubjectId del token (nunca del body).
    public class SecurityAdminService : ISecurityAdminService
    {
        private static readonly Regex CodigoValido = new("^[A-Z][A-Z0-9_]*$", RegexOptions.Compiled);

        private readonly IRolRepository _rolRepository;
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IAccionRepository _accionRepository;
        private readonly IPermissionService _permissionService;
        private readonly IIdentityProvider _identityProvider;
        private readonly ISecurityAuditRepository _securityAuditRepository;

        public SecurityAdminService(
            IRolRepository rolRepository,
            IUsuarioRepository usuarioRepository,
            IAccionRepository accionRepository,
            IPermissionService permissionService,
            IIdentityProvider identityProvider,
            ISecurityAuditRepository securityAuditRepository)
        {
            _rolRepository = rolRepository;
            _usuarioRepository = usuarioRepository;
            _accionRepository = accionRepository;
            _permissionService = permissionService;
            _identityProvider = identityProvider;
            _securityAuditRepository = securityAuditRepository;
        }

        public async Task<RolResponseDto> CrearRolAsync(string actorExternalSubjectId, CreateRolRequestDto request)
        {
            ValidarCodigo(request.Codigo);
            if (string.IsNullOrWhiteSpace(request.Nombre)) throw new ArgumentException("El nombre del rol es obligatorio.");

            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var rol = new Rol { Codigo = request.Codigo, Nombre = request.Nombre, Descripcion = request.Descripcion ?? string.Empty };

            var audit = NuevoAudit(actor, "ROL_CREAR", "Rol", request.Codigo,
                $"nombre={request.Nombre}");

            await _rolRepository.CrearAsync(rol, audit);
            return await ObtenerRolAsync(request.Codigo);
        }

        public async Task<RolResponseDto> EditarRolAsync(string actorExternalSubjectId, string codigo, UpdateRolRequestDto request)
        {
            if (string.IsNullOrWhiteSpace(request.Nombre)) throw new ArgumentException("El nombre del rol es obligatorio.");

            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "ROL_EDITAR", "Rol", codigo,
                $"nombre={request.Nombre}");

            await _rolRepository.EditarAsync(codigo, request.Nombre, request.Descripcion ?? string.Empty, audit);
            return await ObtenerRolAsync(codigo);
        }

        public async Task SetRolActivoAsync(string actorExternalSubjectId, string codigo, bool activo)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, activo ? "ROL_ACTIVAR" : "ROL_DESACTIVAR", "Rol", codigo, $"activo={activo}");

            await _rolRepository.SetActivoAsync(codigo, activo, audit);
        }

        public async Task EliminarRolAsync(string actorExternalSubjectId, string codigo)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "ROL_ELIMINAR", "Rol", codigo, $"codigo={codigo}");

            await _rolRepository.EliminarAsync(codigo, audit);
        }

        public async Task<IReadOnlyList<RolResponseDto>> ListarRolesAsync()
        {
            var roles = await _rolRepository.GetAllAsync();
            var resultado = new List<RolResponseDto>();
            foreach (var rol in roles)
            {
                var acciones = await _rolRepository.GetAccionCodigosAsync(rol.Codigo);
                resultado.Add(MapRol(rol, acciones));
            }
            return resultado;
        }

        public async Task<RolResponseDto> ObtenerRolAsync(string codigo)
        {
            var rol = await _rolRepository.GetByCodigoAsync(codigo);
            if (rol == null) throw new RolNoEncontradoException(codigo);

            var acciones = await _rolRepository.GetAccionCodigosAsync(codigo);
            return MapRol(rol, acciones);
        }

        public async Task<IReadOnlyList<AccionResponseDto>> ListarAccionesAsync()
        {
            var acciones = await _accionRepository.GetAllAsync();
            return acciones.Select(a => new AccionResponseDto { Codigo = a.Codigo, Descripcion = a.Descripcion, Activo = a.Activo }).ToList();
        }

        public async Task<IReadOnlyList<UsuarioResumenResponseDto>> ListarUsuariosAsync()
        {
            var usuarios = await _usuarioRepository.GetAllAsync();
            return usuarios.Select(u => new UsuarioResumenResponseDto
            {
                UsuarioId = u.UsuarioId,
                PersonaId = u.PersonaId,
                Email = u.Email,
                Activo = u.Activo,
                RolesActivos = u.RolesActivos,
            }).ToList();
        }

        public async Task AsignarAccionARolAsync(string actorExternalSubjectId, string rolCodigo, string accionCodigo)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "ROL_ASIGNAR_ACCION", "RolAccion", $"{rolCodigo}/{accionCodigo}",
                $"rol={rolCodigo};accion={accionCodigo}");

            await _rolRepository.AsignarAccionAsync(rolCodigo, accionCodigo, actor.UsuarioId, audit);
        }

        public async Task QuitarAccionDeRolAsync(string actorExternalSubjectId, string rolCodigo, string accionCodigo)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "ROL_QUITAR_ACCION", "RolAccion", $"{rolCodigo}/{accionCodigo}",
                $"rol={rolCodigo};accion={accionCodigo}");

            await _rolRepository.QuitarAccionAsync(rolCodigo, accionCodigo, audit);
        }

        public async Task AsignarRolAUsuarioAsync(string actorExternalSubjectId, string usuarioId, string rolCodigo)
        {
            var rol = await _rolRepository.GetByCodigoAsync(rolCodigo);
            if (rol == null) throw new RolNoEncontradoException(rolCodigo);

            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "USUARIO_ASIGNAR_ROL", "UsuarioRol", $"{usuarioId}/{rolCodigo}",
                $"usuario={usuarioId};rol={rolCodigo}");

            await _usuarioRepository.AsignarRolAsync(usuarioId, rolCodigo, actor.UsuarioId, audit);
        }

        public async Task QuitarRolDeUsuarioAsync(string actorExternalSubjectId, string usuarioId, string rolCodigo)
        {
            var rol = await _rolRepository.GetByCodigoAsync(rolCodigo);
            if (rol == null) throw new RolNoEncontradoException(rolCodigo);

            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, "USUARIO_QUITAR_ROL", "UsuarioRol", $"{usuarioId}/{rolCodigo}",
                $"usuario={usuarioId};rol={rolCodigo}");

            await _usuarioRepository.QuitarRolAsync(usuarioId, rolCodigo, audit);
        }

        public async Task<PermisosEfectivosResponseDto> ConsultarPermisosEfectivosAsync(string usuarioId)
        {
            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null) throw new UsuarioNoEncontradoException(usuarioId);

            var permisos = await _permissionService.GetPermisosEfectivosPorUsuarioIdAsync(usuarioId);
            return new PermisosEfectivosResponseDto
            {
                UsuarioId = permisos.UsuarioId,
                PersonaId = permisos.PersonaId,
                UsuarioActivo = permisos.UsuarioActivo,
                Roles = permisos.Roles,
                Acciones = permisos.Acciones,
            };
        }

        public async Task SetUsuarioActivoAsync(string actorExternalSubjectId, string usuarioId, bool activo)
        {
            var actor = await ResolveActorAsync(actorExternalSubjectId);
            var audit = NuevoAudit(actor, activo ? "USUARIO_ACTIVAR" : "USUARIO_DESACTIVAR", "Usuario", usuarioId, $"activo={activo}");

            await _usuarioRepository.SetActivoAsync(usuarioId, activo, audit);
        }

        public async Task<PasswordResetLinkResponseDto> GenerarPasswordResetLinkAsync(string actorExternalSubjectId, string usuarioId)
        {
            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null) throw new UsuarioNoEncontradoException(usuarioId);

            if (usuario.IdentityProvider != FirebaseIdentityProvider.ProviderName || string.IsNullOrEmpty(usuario.ExternalSubjectId))
            {
                throw new UsuarioSinIdentidadRecuperableException(usuarioId);
            }

            var actor = await ResolveActorAsync(actorExternalSubjectId);

            // Generar el enlace (Firebase, sistema externo) y auditar (Firestore) son dos escrituras
            // independientes -nunca una transacción distribuida-: si el proceso cae justo entre
            // ambas, el enlace ya fue emitido por Firebase pero la auditoría puede faltar. Nunca al
            // revés: si esta llamada falla, la ejecución nunca llega a auditar un éxito que no
            // ocurrió.
            var resetLink = await _identityProvider.GeneratePasswordResetLinkAsync(usuario.ExternalSubjectId);

            // Detalle deliberadamente vacío: nunca el email ni el enlace viajan a security_audits.
            var audit = NuevoAudit(actor, "USUARIO_GENERAR_RESET_PASSWORD", "Usuario", usuarioId, string.Empty);
            await _securityAuditRepository.RegistrarAsync(audit);

            return new PasswordResetLinkResponseDto { ResetLink = resetLink };
        }

        private static RolResponseDto MapRol(Rol rol, IReadOnlyList<string> acciones) => new()
        {
            Codigo = rol.Codigo,
            Nombre = rol.Nombre,
            Descripcion = rol.Descripcion,
            Activo = rol.Activo,
            Acciones = acciones.ToList(),
        };

        private static void ValidarCodigo(string? codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo) || !CodigoValido.IsMatch(codigo))
            {
                throw new ArgumentException("El código del rol es inválido: debe ser no vacío, sin '/' ni caracteres de ruta, y usar solo mayúsculas, números y guion bajo (empezando con una letra).");
            }
        }

        private static SecurityAudit NuevoAudit((string UsuarioId, string PersonaId) actor, string operacion, string targetTipo, string targetId, string detalle) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ActorUsuarioId = actor.UsuarioId,
            ActorPersonaId = actor.PersonaId,
            Operacion = operacion,
            TargetTipo = targetTipo,
            TargetId = targetId,
            Detalle = detalle,
            Timestamp = DateTime.UtcNow,
        };

        // El actor ya pasó por AccionAuthorizationHandler para llegar acá, así que su Usuario ya
        // está provisionado y activo; esta resolución es solo para completar los campos de
        // auditoría (ActorUsuarioId/ActorPersonaId), nunca para tomar una decisión de autorización.
        private async Task<(string UsuarioId, string PersonaId)> ResolveActorAsync(string externalSubjectId)
        {
            var usuarioId = await _usuarioRepository.GetUsuarioIdByExternalSubjectAsync(FirebaseIdentityProvider.ProviderName, externalSubjectId);
            if (usuarioId == null) throw new IdentityNotProvisionedException(externalSubjectId);

            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null) throw new IdentityNotProvisionedException(externalSubjectId);

            return (usuario.Id, usuario.PersonaId);
        }
    }
}
