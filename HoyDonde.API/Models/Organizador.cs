using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Organizador : ApplicationUser
    {
        // Relationships are not stored directly in the user document in this schema
        public virtual List<Control> ControlAccounts { get; set; } = new();
        public virtual List<Event> Events { get; set; } = new();
    }
}
