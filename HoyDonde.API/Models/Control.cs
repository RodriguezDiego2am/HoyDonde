using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class Control : ApplicationUser
    {
        public int EventId { get; set; }
        public virtual Event Event { get; set; } = null!;

        public string OrganizadorId { get; set; } = string.Empty;
        public virtual Organizador Organizador { get; set; } = null!;
    }
}

