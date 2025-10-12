using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class StudyGroupStudyPlan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StudyGroupId { get; set; }
        public Guid StudyPlanStableId { get; set; }
        public int? PinnedVersionNumber { get; set; }
        public string Permission { get; set; } = "view"; // view|comment|edit|admin
        public bool AutoEnroll { get; set; } = false;
        public string? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }

    }
}
