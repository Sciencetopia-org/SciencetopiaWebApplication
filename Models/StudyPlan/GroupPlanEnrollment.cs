using System.ComponentModel.DataAnnotations;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models;

/// <summary>
/// Active plan enrollment for a PersonalGroup.
/// StudyGroupStudyPlans remains the study-group adoption/catalog table.
/// </summary>
public class GroupPlanEnrollment
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid GroupId { get; set; }

    [Required]
    public Guid StudyPlanStableId { get; set; }

    [Required]
    public Guid PlanVersionId { get; set; }

    [Required]
    [MaxLength(16)]
    public string VersionPolicy { get; set; } = "Current";

    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public PlanRole Role { get; set; } = PlanRole.Viewer;

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public GroupEntity? Group { get; set; }
}
