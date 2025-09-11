using Sciencetopia.Models;

public class KnowledgeNode
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultL10nSetId { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }
}

public class KnowledgeNodeDraft
{
    public Guid Id { get; set; }
    public Guid NodeId { get; set; } // 对应主表的 KnowledgeNode.Id
    public string? Name { get; set; }
    public string? Description { get; set; }

    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;

    public string? SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public string? ReviewComment { get; set; }
}

public class KnowledgeNodeVersion
{
    public Guid Id { get; set; } // 当前版本条目的唯一 ID
    public Guid NodeId { get; set; } // 原始主表 KnowledgeNode 的 ID

    public string? Name { get; set; }
    public string? Description { get; set; }

    public DateTimeOffset CreatedDate { get; set; } // 被保存为版本的时间
    public string? PublishedBy { get; set; }
    public DateTimeOffset? PublishedAt { get; set; } // 审核通过的时间点（可以为空）

    public int VersionNumber { get; set; } // 可选：递增版本号，便于比较
}
