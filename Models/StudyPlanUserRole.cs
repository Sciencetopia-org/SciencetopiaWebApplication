using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models
{
    public class StudyPlanUserRole
    {
        public Guid PlanStableId { get; set; }
        public string UserId { get; set; } = string.Empty; // Identity key is string
        public PlanRole Role { get; set; }

        public ApplicationUser User { get; set; } = default!;
    }
}
