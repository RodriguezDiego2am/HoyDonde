using Microsoft.AspNetCore.Identity;
using System;

namespace HoyDonde.API.Models
{
    public abstract class ApplicationUser : IdentityUser, ISoftDeletable
    {
        public bool IsDeleted { get; set; } = false;
        public DateTime? DeletedAt { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
