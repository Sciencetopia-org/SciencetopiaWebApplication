using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class ApplicationUser
    {
        public string? Id { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Salt { get; set; }
    }
}
