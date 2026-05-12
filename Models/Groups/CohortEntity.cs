using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models;

/// <summary>
/// Cohort flavor that hangs off the Groups ancestor.
/// </summary>
public class CohortEntity
{
    [Key]
    [ForeignKey(nameof(Group))]
    public Guid Id { get; set; }

    [Required]
    public Guid StudyGroupStudyPlanId { get; set; }

    [Required]
    public Guid StudyPlanVersionId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(20)]
    public string? Visibility { get; set; } = "private"; // private|group|public

    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public Guid? NameL10nSetId { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime StartAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndAt { get; set; }

    // Enrollment policy: OptIn or Auto
    public CohortEnrollMode EnrollmentPolicy { get; set; } = CohortEnrollMode.OptIn;

    public string? SettingsJson { get; set; }

    public GroupEntity? Group { get; set; }
    public StudyGroupStudyPlan? StudyGroupStudyPlan { get; set; }
}
