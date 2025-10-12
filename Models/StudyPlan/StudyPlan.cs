using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sciencetopia.Models;

public class StudyPlanEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid StableId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public bool IsCurrent { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid CreatorId { get; set; }  // 关联到用户ID（可以后续添加外键User）

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

}

public class LessonEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid StableId { get; set; }
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "Draft";
    public bool IsCurrent { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? RetiredAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
}
