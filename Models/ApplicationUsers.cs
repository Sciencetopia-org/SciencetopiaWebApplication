using Microsoft.AspNetCore.Identity;
using System;

namespace Sciencetopia.Models
{
    public class ApplicationUser : IdentityUser
    {
        // Keep your custom properties
        public string? AvatarUrl { get; set; } // User avatar
        public string? SelfIntroduction { get; set; }
        public string? Gender { get; set; }
        public DateTime BirthDate { get; set; }
        public DateTime? LastUsernameChangeDate { get; set; }

        // The Id, UserName, Email, and Password are already included in IdentityUser
    }
}
