using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sciencetopia.Models;

public class StudyPlanEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; }

    public string Description { get; set; }

    public Guid CreatorId { get; set; }  // 关联到用户ID（可以后续添加外键User）

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    // Current published version pointer (optional, backfilled to latest)
    public long? CurrentVersionId { get; set; }

    // RBAC user roles
    public ICollection<StudyPlanUserRole> UserRoles { get; set; } = new List<StudyPlanUserRole>();
}

public class LessonEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; }

    public string Description { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}
