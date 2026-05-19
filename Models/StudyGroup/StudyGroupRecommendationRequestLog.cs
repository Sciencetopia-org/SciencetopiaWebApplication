using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

public class StudyGroupRecommendationRequestLog
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RequestId { get; set; }

    [MaxLength(450)]
    public string? UserId { get; set; }

    [MaxLength(32)]
    public string Scene { get; set; } = "discover";

    [MaxLength(32)]
    public string SortMode { get; set; } = "personalized";

    [MaxLength(512)]
    public string? Query { get; set; }

    [MaxLength(64)]
    public string StrategyVersion { get; set; } = string.Empty;

    public int CandidateCount { get; set; }

    public int ReturnedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
