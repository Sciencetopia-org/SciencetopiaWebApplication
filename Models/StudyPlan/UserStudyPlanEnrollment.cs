using System;
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

/// <summary>
/// User enrollment to plan/cohort within a group (规范：UserStudyPlanEnrollments).
/// </summary>
public class UserStudyPlanEnrollment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    public Guid GroupId { get; set; }

    public Guid StudyPlanStableId { get; set; }

    public Guid? CohortGroupId { get; set; }

    public Guid PlanVersionId { get; set; }

    // Learner / TA / Instructor
    [MaxLength(16)]
    public string Role { get; set; } = "Learner";

    // Active / Completed / Dropped
    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public string? ProgressJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
