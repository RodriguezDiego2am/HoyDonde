using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    [Google.Cloud.Firestore.FirestoreData]
    public class Control : ApplicationUser
    {
        [Google.Cloud.Firestore.FirestoreProperty]
        public string EventId { get; set; } = string.Empty;
        
        // Navigation properties ignored
        public virtual Event Event { get; set; } = null!;

        [Google.Cloud.Firestore.FirestoreProperty]
        public string OrganizadorId { get; set; } = string.Empty;
        
        public virtual Organizador Organizador { get; set; } = null!;
    }
}
