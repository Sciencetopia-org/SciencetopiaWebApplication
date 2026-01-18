using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models
{
    /// <summary>
    /// Group-to-Plan binding (Group-first规范：GroupStudyPlanRelations).
    /// </summary>
    public class StudyGroupStudyPlan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StudyGroupId { get; set; }
        public Guid StudyPlanStableId { get; set; }
        public int? PinnedVersionNumber { get; set; }

        // Active plan version for the group
        public Guid? PlanVersionId { get; set; }

        [NotMapped]
        public Guid? ActivePlanVersionId
        {
            get => PlanVersionId;
            set => PlanVersionId = value;
        }

        public bool ResolveToHead { get; set; } = false;

        // Offer | Adopt | Default
        public string RelationType { get; set; } = "Adopt";

        // Group-level settings / overrides
        public string? SettingsJson { get; set; }

        public string Permission { get; set; } = "view"; // view|comment|edit|admin
        public bool AutoEnroll { get; set; } = false;
        public string? CreatedBy { get; set; }
        public DateTime CreatedDate { get; set; }
        public DateTime UpdatedDate { get; set; }

    }
}
