using System;
using System.ComponentModel.DataAnnotations;
using Sciencetopia.Models.Enums;

namespace Sciencetopia.Models;

/// <summary>
/// Unified user-group membership for all group types.
/// </summary>
public class UserGroupEntity
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public Guid GroupId { get; set; }

    [Required]
    public GroupRole Role { get; set; } = GroupRole.Member;

    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = "Active";

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LeftAt { get; set; }

    public ApplicationUser? User { get; set; }
    public GroupEntity? Group { get; set; }
}
