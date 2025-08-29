using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models
{
    public class StudyGroupEntity
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ImageUrl { get; set; }

        public string? Status { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // RBAC user roles
        public ICollection<StudyGroupUserRole> UserRoles { get; set; } = new List<StudyGroupUserRole>();
    }
}
