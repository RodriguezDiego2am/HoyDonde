namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Admin : ApplicationUser
    {
        [Google.Cloud.Firestore.FirestoreProperty]
        public string BiometricIdentifier { get; set; } = string.Empty;
    }
}

