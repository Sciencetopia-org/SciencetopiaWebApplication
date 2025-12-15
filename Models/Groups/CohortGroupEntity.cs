using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models;

/// <summary>
/// Cohort/班级 group flavor that hangs off the Groups ancestor.
/// </summary>
public class CohortGroupEntity
{
    [Key]
    [ForeignKey(nameof(Group))]
    public Guid Id { get; set; }

    // Parent Group (usually StudyGroup/Org)
    public Guid ParentGroupId { get; set; }

    // Curriculum binding
    public Guid StudyPlanStableId { get; set; }
    public Guid? StudyPlanVersionId { get; set; }

    public Guid? NameL10nSetId { get; set; }

    public DateTime? StartAt { get; set; }
    public DateTime? EndAt { get; set; }

    public string? SettingsJson { get; set; }

    public GroupEntity? Group { get; set; }
}
