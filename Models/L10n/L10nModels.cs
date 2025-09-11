using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models.L10n
{
    public class L10nSet
    {
        [Key]
        public Guid L10nSetId { get; set; } = Guid.NewGuid();
        [MaxLength(50)]
        public string Scope { get; set; } = "knowledge_node"; // default for nodes
        public string? PolicyJson { get; set; }
        public bool IsManaged { get; set; } = false;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }

    public enum L10nItemKind : byte
    {
        Primary = 0,
        Alias = 1,
        Abbreviation = 2,
        Historical = 3
    }

    public class L10nItem
    {
        [Key]
        public Guid L10nItemId { get; set; } = Guid.NewGuid();
        [MaxLength(32)]
        public string FieldKey { get; set; } = "name";
        [MaxLength(10)]
        public string? LangCode { get; set; }
        [MaxLength(10)]
        public string? ScriptCode { get; set; }
        public L10nItemKind Kind { get; set; } = L10nItemKind.Primary;
        [MaxLength(200)]
        public string? Text { get; set; }
        public string? Content { get; set; }
        public int SortOrder { get; set; } = 0;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }

    public class NodeL10nSet
    {
        public Guid NodeId { get; set; }
        public Guid L10nSetId { get; set; }
        public byte Relation { get; set; } = 0; // 0 primary
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class L10nSetItem
    {
        public Guid L10nSetId { get; set; }
        public Guid L10nItemId { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class TagL10nSet
    {
        public Guid TagId { get; set; }
        public Guid L10nSetId { get; set; }
        public byte Relation { get; set; } = 0; // 0 primary
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
