using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Cliente : ApplicationUser
    {
        [Google.Cloud.Firestore.FirestoreProperty]
        public string FullName { get; set; } = string.Empty;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string DNI { get; set; } = string.Empty;
        
        // PhoneNumber is in base class
        
        [Google.Cloud.Firestore.FirestoreProperty]
        public string Identifier { get; set; } = Guid.NewGuid().ToString();

        public virtual List<Ticket> Tickets { get; set; } = new();
    }
}
