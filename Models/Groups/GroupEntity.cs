using System;
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

/// <summary>
/// Unified Groups ancestor table (Group-first建模).
/// </summary>
public class GroupEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    // StudyGroup | CohortGroup | OrgGroup | ...
    [Required]
    [MaxLength(32)]
    public string Kind { get; set; } = "StudyGroup";

    [MaxLength(450)]
    public string? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
