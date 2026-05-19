using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

public class StudyGroupRecommendationFeedback
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    public Guid GroupId { get; set; }

    [Required]
    [MaxLength(32)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Scene { get; set; } = "discover";

    public int? Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
