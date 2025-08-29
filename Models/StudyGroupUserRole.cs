using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models
{
    public class StudyGroupUserRole
    {
        public Guid GroupId { get; set; }
        public string UserId { get; set; } = string.Empty; // Identity key is string
        public GroupRole Role { get; set; }

        public StudyGroupEntity Group { get; set; } = default!;
        public ApplicationUser User { get; set; } = default!;
    }
}

