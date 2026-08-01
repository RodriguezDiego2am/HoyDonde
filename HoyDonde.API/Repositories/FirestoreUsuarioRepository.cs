using Google.Cloud.Firestore;
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
    }
}
