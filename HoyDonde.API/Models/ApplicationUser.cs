using Microsoft.AspNetCore.Identity;
using System;

namespace HoyDonde.API.Models
{
    // Base class for users in Firestore
    [Google.Cloud.Firestore.FirestoreData]
    public abstract class ApplicationUser : ISoftDeletable
    {
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Id { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string UserName { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Email { get; set; } = string.Empty;
        
        [Google.Cloud.Firestore.FirestoreProperty]
        public string PhoneNumber { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string Role { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool IsDeleted { get; set; } = false;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime? DeletedAt { get; set; }

        [Google.Cloud.Firestore.FirestoreProperty]
        public bool IsActive { get; set; } = true;

        [Google.Cloud.Firestore.FirestoreProperty]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
