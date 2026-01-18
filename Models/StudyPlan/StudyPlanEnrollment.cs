using System;
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

/// <summary>
/// User enrollment to a concrete study plan delivery scope (cohort or personal).
/// </summary>
public class StudyPlanEnrollment
{
    [Key]
    public Guid EnrollmentId { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string ScopeType { get; set; } = "Cohort"; // Cohort | Personal

    [Required]
    public Guid ScopeId { get; set; }

    [Required]
    public Guid PlanVersionId { get; set; }

    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public DateTimeOffset EnrolledAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; set; }
}
