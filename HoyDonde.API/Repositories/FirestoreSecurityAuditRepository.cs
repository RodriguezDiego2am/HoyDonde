using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public class FirestoreSecurityAuditRepository : ISecurityAuditRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "security_audits";

        public FirestoreSecurityAuditRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<IReadOnlyList<SecurityAudit>> GetByRangoAsync(DateTime desde, DateTime hasta)
        {
            var snapshot = await _firestore.Collection(CollectionName)
                .WhereGreaterThanOrEqualTo(nameof(SecurityAudit.Timestamp), desde)
                .WhereLessThan(nameof(SecurityAudit.Timestamp), hasta)
                .OrderByDescending(nameof(SecurityAudit.Timestamp))
                .GetSnapshotAsync();

            return snapshot.Documents.Select(d => d.ConvertTo<SecurityAudit>()).ToList();
        }
    }
}
