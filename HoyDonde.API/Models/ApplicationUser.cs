using Microsoft.AspNetCore.Identity;
using System;

namespace HoyDonde.API.Models
{
    public abstract class ApplicationUser : IdentityUser
    {
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
