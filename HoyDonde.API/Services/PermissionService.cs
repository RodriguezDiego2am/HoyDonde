using HoyDonde.API.Repositories;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Implementa la resolución de permisos de docs/security-refactor-plan.md §4, paso a paso:
    // identidad externa -> Usuario (IsActive) -> roles activos -> Rol.Activo -> acciones del rol
    // -> Accion.Activo. Todas las lecturas son directas por ID; ninguna es un query sobre la
    // colección completa. Sin caché en esta etapa.
    public class PermissionService : IPermissionService
    {
        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IRolRepository _rolRepository;
        private readonly IAccionRepository _accionRepository;

        public PermissionService(IUsuarioRepository usuarioRepository, IRolRepository rolRepository, IAccionRepository accionRepository)
        {
            _usuarioRepository = usuarioRepository;
            _rolRepository = rolRepository;
            _accionRepository = accionRepository;
        }

        public async Task<bool> TieneAccionAsync(string identityProvider, string externalSubjectId, string accionCodigo)
        {
            var permisos = await GetPermisosEfectivosAsync(identityProvider, externalSubjectId);
            return permisos.Acciones.Contains(accionCodigo);
        }

        public async Task<PermisosEfectivosResult> GetPermisosEfectivosAsync(string identityProvider, string externalSubjectId)
        {
            var usuarioId = await _usuarioRepository.GetUsuarioIdByExternalSubjectAsync(identityProvider, externalSubjectId);
            if (usuarioId == null)
            {
                return new PermisosEfectivosResult(null, null, false, Array.Empty<string>(), Array.Empty<string>());
            }

            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null || !usuario.IsActive)
            {
                return new PermisosEfectivosResult(usuarioId, usuario?.PersonaId, false, Array.Empty<string>(), Array.Empty<string>());
            }

            var rolCodigos = await _usuarioRepository.GetRolCodigosActivosAsync(usuarioId);
            var roles = new List<string>();
            var acciones = new HashSet<string>();

            foreach (var rolCodigo in rolCodigos)
            {
                var rol = await _rolRepository.GetByCodigoAsync(rolCodigo);
                if (rol == null || !rol.Activo) continue;

                roles.Add(rolCodigo);

                var accionCodigosDelRol = await _rolRepository.GetAccionCodigosAsync(rolCodigo);
                foreach (var accionCodigo in accionCodigosDelRol)
                {
                    if (acciones.Contains(accionCodigo)) continue;

                    var accion = await _accionRepository.GetByCodigoAsync(accionCodigo);
                    if (accion != null && accion.Activo)
                    {
                        acciones.Add(accionCodigo);
                    }
                }
            }

            return new PermisosEfectivosResult(usuarioId, usuario.PersonaId, true, roles, acciones.ToList());
        }
    }
}
