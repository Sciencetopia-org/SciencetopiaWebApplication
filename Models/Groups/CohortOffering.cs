using System;
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

/// <summary>
/// Delivery offering for a cohort, bound to a specific plan version.
/// </summary>
public class CohortOffering
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid CohortGroupId { get; set; }

    [Required]
    public Guid StudyGroupStudyPlanId { get; set; }

    [Required]
    public Guid StudyPlanVersionId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Active";

    public DateTime StartAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    public CohortEntity? Cohort { get; set; }
    public StudyGroupStudyPlan? StudyGroupStudyPlan { get; set; }
}
