using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models;

public class StudyGroupEmbedding
{
    [Key]
    public Guid GroupId { get; set; }

    [MaxLength(64)]
    public string Provider { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Model { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? VectorStoreId { get; set; }

    public string SourceText { get; set; } = string.Empty;

    [MaxLength(128)]
    public string SourceHash { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
