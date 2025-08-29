using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class StudyGroupStudyPlan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StudyGroupId { get; set; }
        public Guid StudyPlanId { get; set; }
        public string Permission { get; set; } = "view"; // view|comment|edit|admin
        public bool AutoEnroll { get; set; } = false;
        public bool UseDraftFlow { get; set; } = true;
        public string? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }
    }
}

