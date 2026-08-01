using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreIdentidadHuerfanaRepository : IIdentidadHuerfanaRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "identidades_huerfanas";

        public FirestoreIdentidadHuerfanaRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task RegistrarAsync(IdentidadHuerfana identidadHuerfana)
        {
            var docRef = _firestore.Collection(CollectionName).Document();
            identidadHuerfana.Id = docRef.Id;
            await docRef.SetAsync(identidadHuerfana);
        }
    }
}
