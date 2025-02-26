namespace HoyDonde.API.Models
{
    public class Admin : ApplicationUser
    {
        public string BiometricIdentifier { get; set; } = string.Empty;
    }
}

