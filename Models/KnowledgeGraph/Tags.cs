using Sciencetopia.Models;

public class Tags
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultL10nSetId { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
}

public class TagDraft
{
    public Guid Id { get; set; }
    public Guid TagId { get; set; } // 对应主表 Tags.Id
    public string? Name { get; set; }
    public string? Description { get; set; }

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;

    public string? SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewComment { get; set; }
}

public class TagVersion
{
    public Guid Id { get; set; }
    public Guid TagId { get; set; }

    public string? Name { get; set; }
    public string? Description { get; set; }

    public DateTimeOffset CreatedDate { get; set; }
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public int VersionNumber { get; set; }
}
