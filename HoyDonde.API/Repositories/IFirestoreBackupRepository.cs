using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.Models;

namespace HoyDonde.API.Repositories
{
    public interface IFirestoreBackupRepository
    {
        Task CreateAsync(FirestoreBackup backup);

        /// <summary>
        /// Creates a restore and its pre-restore safety export in a single Firestore transaction,
        /// but only while no other restore is still Pending or Running. Returns false otherwise.
        ///
        /// Two guarantees in one transaction, because an import overwrites production data. The
        /// pair is atomic: a restore whose safety export was never written can neither advance nor
        /// fail, and would stay Pending forever. And the claim is global, not per record: claiming
        /// each restore document separately would still let two concurrent requests create two
        /// distinct restores and fire two imports. The guard document is what serialises that.
        /// </summary>
        Task<bool> TryCreateRestorePairAsync(FirestoreBackup restore, FirestoreBackup safetyExport);

        /// <summary>
        /// Transactional compare-and-set on the record's state. Returns false when the record is
        /// gone or is no longer in <paramref name="expectedState"/>, so a concurrent caller that
        /// already claimed the transition is the only one that acts on it.
        /// </summary>
        Task<bool> TryTransitionAsync(string id, FirestoreBackup.BackupState expectedState, Action<FirestoreBackup> apply);
        Task<FirestoreBackup?> GetByIdAsync(string id);
        Task<IReadOnlyList<FirestoreBackup>> GetAllAsync();
        Task UpdateAsync(FirestoreBackup backup);
        Task DeleteAsync(string id);
    }
}
