using System;
using System.ComponentModel.DataAnnotations;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models;

/// <summary>
/// Group-first membership table: who belongs to which group and with what role/status.
/// </summary>
public class GroupMemberEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid GroupId { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public GroupRole Role { get; set; } = GroupRole.Member;

    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LeftAt { get; set; }

    public StudyGroupEntity? StudyGroup { get; set; }
    public ApplicationUser? User { get; set; }
}
