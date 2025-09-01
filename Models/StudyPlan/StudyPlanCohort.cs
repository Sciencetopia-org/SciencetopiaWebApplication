using System.ComponentModel.DataAnnotations;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models;

public class StudyPlanCohort
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid StudyPlanId { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(20)]
    public string? Visibility { get; set; } = "private"; // private|group|public

    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    [MaxLength(450)]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public StudyPlanEntity? Plan { get; set; }

    // Group-scoped cohort (optional)
    public Guid? StudyGroupId { get; set; }

    // Enrollment mode: Auto or OptIn
    public CohortEnrollMode EnrollMode { get; set; } = CohortEnrollMode.OptIn;

    // Version pinning and member count
    public long? PinnedVersionId { get; set; }
    public int MembersCount { get; set; }
}
