using Google.Cloud.Firestore;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreUsuarioRepository : IUsuarioRepository
    {
        private readonly FirestoreDb _firestore;
        private const string PersonasCollection = "personas";
        private const string UsuariosCollection = "usuarios";
        private const string RolesSubcollectionName = "roles";
        private const string IdentidadesExternasCollection = "identidades_externas";

        public FirestoreUsuarioRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        private static string IdentidadExternaId(string identityProvider, string externalSubjectId)
            => $"{identityProvider}#{externalSubjectId}";

        // Todo-o-nada: Persona, Usuario, UsuarioRol e IdentidadExterna se crean en la MISMA
        // transacción Firestore. Si cualquiera de las escrituras falla al confirmar, Firestore
        // descarta la transacción completa (docs/security-refactor-plan.md §6, Etapa 2).
        // PersonaId/UsuarioId ya vienen generados en el request -no se generan acá- para que
        // quien los generó (y los precargue en un test, por ejemplo) pueda razonar sobre ellos
        // antes de que exista la transacción.
        public Task<UsuarioProvisioningResult> ProvisionarAsync(UsuarioProvisioningRequest request)
        {
            var identidadRef = _firestore.Collection(IdentidadesExternasCollection)
                .Document(IdentidadExternaId(request.IdentityProvider, request.ExternalSubjectId));

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var identidadSnapshot = await transaction.GetSnapshotAsync(identidadRef);
                if (identidadSnapshot.Exists)
                {
                    // Reintento idempotente: esta identidad externa ya fue provisionada en una
                    // corrida anterior. Se devuelven los IDs ya existentes, nunca los del
                    // request actual, y no se escribe ni se cambia ningún rol.
                    var identidadExistente = identidadSnapshot.ConvertTo<IdentidadExterna>();
                    var usuarioExistenteRef = _firestore.Collection(UsuariosCollection).Document(identidadExistente.UsuarioId);
                    var usuarioExistenteSnapshot = await transaction.GetSnapshotAsync(usuarioExistenteRef);
                    var usuarioExistente = usuarioExistenteSnapshot.ConvertTo<Usuario>();
                    return new UsuarioProvisioningResult(usuarioExistente.PersonaId, usuarioExistente.Id);
                }

                var persona = new Persona
                {
                    Id = request.PersonaId,
                    FullName = request.FullName ?? string.Empty,
                    DNI = request.Dni ?? string.Empty,
                    PhoneNumber = request.PhoneNumber ?? string.Empty,
                    Email = request.Email,
                };

                var usuario = new Usuario
                {
                    Id = request.UsuarioId,
                    PersonaId = request.PersonaId,
                    IdentityProvider = request.IdentityProvider,
                    ExternalSubjectId = request.ExternalSubjectId,
                    Email = request.Email,
                };

                var usuarioRol = new UsuarioRol
                {
                    RolCodigo = request.RolCodigo,
                    AssignedBy = request.AssignedBy,
                };

                var identidadExterna = new IdentidadExterna
                {
                    Id = IdentidadExternaId(request.IdentityProvider, request.ExternalSubjectId),
                    UsuarioId = request.UsuarioId,
                };

                var personaRef = _firestore.Collection(PersonasCollection).Document(request.PersonaId);
                var usuarioRef = _firestore.Collection(UsuariosCollection).Document(request.UsuarioId);
                var usuarioRolRef = usuarioRef.Collection(RolesSubcollectionName).Document(request.RolCodigo);

                // Create (no Set): ninguno de estos documentos debe existir todavía en esta
                // rama -ya se descartó el caso "ya existe" arriba- así que un choque acá debe
                // hacer fallar la transacción entera en vez de sobrescribir en silencio.
                transaction.Create(personaRef, persona);
                transaction.Create(usuarioRef, usuario);
                transaction.Create(usuarioRolRef, usuarioRol);
                transaction.Create(identidadRef, identidadExterna);

                return new UsuarioProvisioningResult(request.PersonaId, request.UsuarioId);
            });
        }

        public async Task<string?> GetUsuarioIdByExternalSubjectAsync(string identityProvider, string externalSubjectId)
        {
            var snapshot = await _firestore.Collection(IdentidadesExternasCollection)
                .Document(IdentidadExternaId(identityProvider, externalSubjectId)).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<IdentidadExterna>().UsuarioId : null;
        }

        public async Task<Usuario?> GetByIdAsync(string usuarioId)
        {
            var snapshot = await _firestore.Collection(UsuariosCollection).Document(usuarioId).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<Usuario>() : null;
        }

        public async Task<IReadOnlyList<string>> GetRolCodigosActivosAsync(string usuarioId)
        {
            var snapshot = await _firestore.Collection(UsuariosCollection).Document(usuarioId)
                .Collection(RolesSubcollectionName).GetSnapshotAsync();
            return snapshot.Documents
                .Select(d => d.ConvertTo<UsuarioRol>())
                .Where(r => r.Activo)
                .Select(r => r.RolCodigo)
                .ToList();
        }

        // Collection group query sobre usuarios/*/roles: el RolCodigo vive únicamente como ID
        // del documento (no como campo), así que el filtro por rol se aplica del lado del
        // cliente sobre los resultados ya acotados por Activo == true. El UsuarioId de cada
        // asignación se obtiene del padre del documento (usuarios/{UsuarioId}/roles/{RolCodigo}).
        //
        // OJO: la colección raíz "roles" (catálogo de Rol, Etapa 1) tiene el mismo Id de
        // colección ("roles") que esta subcolección, así que la collection group query también
        // devuelve esos documentos (un Rol con Codigo == rolCodigo y Activo == true calza con
        // ambos filtros). Un documento de esa colección raíz no tiene padre
        // (Reference.Parent.Parent es null), así que se descarta explícitamente en vez de
        // asumir que todo resultado viene de usuarios/*/roles.
        public async Task<IReadOnlyList<string>> GetUsuarioIdsConRolActivoAsync(string rolCodigo)
        {
            var snapshot = await _firestore.CollectionGroup(RolesSubcollectionName)
                .WhereEqualTo(nameof(UsuarioRol.Activo), true)
                .GetSnapshotAsync();

            return snapshot.Documents
                .Where(d => d.Id == rolCodigo)
                .Select(d => d.Reference.Parent.Parent)
                .Where(usuarioRef => usuarioRef != null && usuarioRef.Parent.Id == UsuariosCollection)
                .Select(usuarioRef => usuarioRef!.Id)
                .Distinct()
                .ToList();
        }

        public Task AsignarRolAsync(string usuarioId, string rolCodigo, string assignedBy, SecurityAudit auditEntry)
        {
            var usuarioRef = _firestore.Collection(UsuariosCollection).Document(usuarioId);
            var usuarioRolRef = usuarioRef.Collection(RolesSubcollectionName).Document(rolCodigo);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var usuarioSnapshot = await transaction.GetSnapshotAsync(usuarioRef);
                if (!usuarioSnapshot.Exists) throw new UsuarioNoEncontradoException(usuarioId);

                var usuarioRolSnapshot = await transaction.GetSnapshotAsync(usuarioRolRef);

                if (!usuarioRolSnapshot.Exists)
                {
                    transaction.Create(usuarioRolRef, new UsuarioRol { RolCodigo = rolCodigo, AssignedBy = assignedBy });
                }
                else if (!usuarioRolSnapshot.ConvertTo<UsuarioRol>().Activo)
                {
                    transaction.Update(usuarioRolRef, new Dictionary<string, object>
                    {
                        { nameof(UsuarioRol.Activo), true },
                        { nameof(UsuarioRol.AssignedBy), assignedBy },
                        { nameof(UsuarioRol.AssignedAt), System.DateTime.UtcNow },
                    });
                }
                else
                {
                    // No-op idempotente: ya estaba activa. No se toca AssignedAt/AssignedBy ni
                    // se audita una mutación que no ocurrió.
                    return;
                }

                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task QuitarRolAsync(string usuarioId, string rolCodigo, SecurityAudit auditEntry)
        {
            var usuarioRef = _firestore.Collection(UsuariosCollection).Document(usuarioId);
            var usuarioRolRef = usuarioRef.Collection(RolesSubcollectionName).Document(rolCodigo);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var usuarioSnapshot = await transaction.GetSnapshotAsync(usuarioRef);
                if (!usuarioSnapshot.Exists) throw new UsuarioNoEncontradoException(usuarioId);

                var usuarioRolSnapshot = await transaction.GetSnapshotAsync(usuarioRolRef);
                var asignacionActiva = usuarioRolSnapshot.Exists && usuarioRolSnapshot.ConvertTo<UsuarioRol>().Activo;

                // El guard solo tiene sentido si ESTA asignación está activa hoy: si ya estaba
                // inactiva (o nunca existió), quitarla no reduce ningún conteo de Administradores
                // efectivos, así que no corresponde evaluar la invariante (evita bloquear
                // operaciones no relacionadas si, por cualquier motivo ajeno, el conteo global ya
                // fuera cero).
                if (!asignacionActiva)
                {
                    // No-op idempotente: no existe o ya estaba inactiva. Nada que quitar, nada
                    // que auditar.
                    return;
                }

                if (rolCodigo == UltimoAdministradorGuard.RolAdministrador)
                {
                    var efectivos = await UltimoAdministradorGuard.ContarEfectivosAsync(
                        transaction, _firestore,
                        usuarioIdConAsignacionForzada: usuarioId, asignacionForzadaActiva: false);
                    if (efectivos == 0) throw new UltimoAdministradorException();
                }

                transaction.Update(usuarioRolRef, new Dictionary<string, object> { { nameof(UsuarioRol.Activo), false } });
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task SetActivoAsync(string usuarioId, bool activo, SecurityAudit auditEntry)
        {
            var usuarioRef = _firestore.Collection(UsuariosCollection).Document(usuarioId);
            var administradorRolRef = usuarioRef.Collection(RolesSubcollectionName).Document(UltimoAdministradorGuard.RolAdministrador);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var usuarioSnapshot = await transaction.GetSnapshotAsync(usuarioRef);
                if (!usuarioSnapshot.Exists) throw new UsuarioNoEncontradoException(usuarioId);

                if (usuarioSnapshot.ConvertTo<Usuario>().IsActive == activo)
                {
                    // No-op idempotente: ya está en el estado pedido. No corresponde tocar
                    // metadata, evaluar el guard del último Administrador, ni auditar.
                    return;
                }

                if (!activo)
                {
                    var administradorRolSnapshot = await transaction.GetSnapshotAsync(administradorRolRef);
                    var esAdministradorActivo = administradorRolSnapshot.Exists && administradorRolSnapshot.ConvertTo<UsuarioRol>().Activo;

                    // Igual que en QuitarRolAsync: si este Usuario no tiene hoy una asignación
                    // ADMINISTRADOR activa, desactivarlo no reduce ningún conteo de
                    // Administradores efectivos, así que no corresponde evaluar la invariante.
                    if (esAdministradorActivo)
                    {
                        var efectivos = await UltimoAdministradorGuard.ContarEfectivosAsync(
                            transaction, _firestore,
                            usuarioIdConEstadoForzado: usuarioId, usuarioForzadoActivo: false);
                        if (efectivos == 0) throw new UltimoAdministradorException();
                    }
                }

                transaction.Update(usuarioRef, new Dictionary<string, object> { { nameof(Usuario.IsActive), activo } });
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public async Task<IReadOnlyList<UsuarioResumen>> GetAllAsync()
        {
            var snapshot = await _firestore.Collection(UsuariosCollection).GetSnapshotAsync();
            var resultado = new List<UsuarioResumen>();

            foreach (var doc in snapshot.Documents)
            {
                var usuario = doc.ConvertTo<Usuario>();
                var rolesActivos = await GetRolCodigosActivosAsync(usuario.Id);
                resultado.Add(new UsuarioResumen(usuario.Id, usuario.PersonaId, usuario.Email, usuario.IsActive, rolesActivos));
            }

            return resultado;
        }
    }
}
