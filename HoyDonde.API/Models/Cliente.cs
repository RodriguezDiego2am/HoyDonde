using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    public class Cliente : ApplicationUser
    {
        public string FullName { get; set; } = string.Empty;
        public string DNI { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Identifier { get; set; } = Guid.NewGuid().ToString();

        public virtual List<Ticket> Tickets { get; set; } = new();
    }
}
