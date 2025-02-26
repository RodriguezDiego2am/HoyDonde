using System.Collections.Generic;

namespace HoyDonde.API.Models
{
    public class Organizador : ApplicationUser
    {
        public virtual List<Control> ControlAccounts { get; set; } = new();
        public virtual List<Event> Events { get; set; } = new();
    }
}
