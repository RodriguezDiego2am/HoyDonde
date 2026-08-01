using Google.Cloud.Firestore;
using Grpc.Core;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreAccionRepository : IAccionRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "acciones";

        public FirestoreAccionRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<bool> ExistsAsync(string codigo)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(codigo).GetSnapshotAsync();
            return snapshot.Exists;
        }

        public async Task CreateAsync(Accion accion)
        {
            var docRef = _firestore.Collection(CollectionName).Document(accion.Codigo);
            try
            {
                await docRef.CreateAsync(accion);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                throw new AccionYaExisteException(accion.Codigo);
            }
        }

        public async Task<Accion?> GetByCodigoAsync(string codigo)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(codigo).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<Accion>() : null;
        }

        public async Task<IReadOnlyList<Accion>> GetAllAsync()
        {
            var snapshot = await _firestore.Collection(CollectionName).GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.ConvertTo<Accion>()).ToList();
        }
    }
}
