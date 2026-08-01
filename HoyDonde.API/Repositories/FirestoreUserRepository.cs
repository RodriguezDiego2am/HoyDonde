using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreUserRepository : IUserRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "users";

        public FirestoreUserRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task CreateUserAsync(ApplicationUser user)
        {
            // Ensure ID is set
            if (string.IsNullOrEmpty(user.Id))
            {
                // In a real scenario, ID should come from Firebase Auth UID
                // If not, auto-generate here?? No, better to force ID.
                throw new System.ArgumentException("User ID must be set (should be Firebase Auth UID)");
            }
            
            var docRef = _firestore.Collection(CollectionName).Document(user.Id);
            await docRef.SetAsync(user);

            // Audit the action
            await AddUserAuditAsync(user.Id, "CREATE", "New user registered in the system", user.Id, user);
        }

        public async Task<ApplicationUser?> GetUserByEmailAsync(string email)
        {
            var query = _firestore.Collection(CollectionName).WhereEqualTo("Email", email).Limit(1);
            var snapshot = await query.GetSnapshotAsync();
            
            if (snapshot.Count == 0) return null;

            return MapDocumentToUser(snapshot.Documents[0]);
        }

        public async Task<ApplicationUser?> GetUserByIdAsync(string id)
        {
            var docRef = _firestore.Collection(CollectionName).Document(id);
            var snapshot = await docRef.GetSnapshotAsync();
            
            if (!snapshot.Exists) return null;

            return MapDocumentToUser(snapshot);
        }

        private ApplicationUser? MapDocumentToUser(DocumentSnapshot doc)
        {
            if (!doc.Exists) return null;

            if (doc.TryGetValue("Role", out string role))
            {
                return role switch
                {
                    Roles.Admin => doc.ConvertTo<Admin>(),
                    Roles.Organizador => doc.ConvertTo<Organizador>(),
                    Roles.Cliente => doc.ConvertTo<Cliente>(),
                    Roles.Control => doc.ConvertTo<Control>(),
                    _ => doc.ConvertTo<Organizador>() // Fallback
                };
            }
            return doc.ConvertTo<Organizador>(); // Fallback
        }

        public async Task<Organizador?> GetOrganizerByIdAsync(string id)
        {
            var docRef = _firestore.Collection(CollectionName).Document(id);
            var snapshot = await docRef.GetSnapshotAsync();

            if (!snapshot.Exists) return null;

            return snapshot.ConvertTo<Organizador>();
        }

        public async Task<bool> IsAdminCreatedAsync()
        {
            // Implementation depends on how we define Admin. 
            // Maybe check for a user with 'Admin' role claim in a subcollection?
            // Or just check if any user exists with specific email.
            // Placeholder:
            return false;
        }

        public async Task UpdateUserAsync(ApplicationUser user, string performedByUserId)
        {
            var docRef = _firestore.Collection(CollectionName).Document(user.Id);
            await docRef.SetAsync(user, SetOptions.MergeAll);
            await AddUserAuditAsync(user.Id, "UPDATE", "User properties updated manually", performedByUserId, user);
        }

        public async Task AddUserAuditAsync(string userId, string actionType, string description, string performedBy, object snapshot)
        {
            var audit = new UserAudit
            {
                Id = System.Guid.NewGuid().ToString(),
                AffectedUserId = userId,
                ActionType = actionType,
                ActionDescription = description,
                PerformedBy = performedBy,
                Timestamp = System.DateTime.UtcNow,
                SnapshotData = System.Text.Json.JsonSerializer.Serialize(snapshot)
            };

            await _firestore.Collection("user_audits").Document(audit.Id).SetAsync(audit);
        }
    }
}
