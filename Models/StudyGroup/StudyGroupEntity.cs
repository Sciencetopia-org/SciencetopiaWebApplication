using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models
{
    /// <summary>
    /// StudyGroup is a concrete flavor of Groups (Group-first建模).
    /// </summary>
    public class StudyGroupEntity
    {
        [Key]
        [ForeignKey(nameof(Group))]
        public Guid Id { get; set; } = Guid.NewGuid();

        // L10n binding per规范; keep legacy Name/Description as fallbacks
        public Guid? NameL10nSetId { get; set; }
        public Guid? DescriptionL10nSetId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ImageUrl { get; set; }

        public string? Status { get; set; }

        // Public / Private / Unlisted
        [MaxLength(16)]
        public string Visibility { get; set; } = "Private";

        // Open / Request / InviteOnly
        [MaxLength(16)]
        public string JoinPolicy { get; set; } = "Request";

        public string? SettingsJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public GroupEntity? Group { get; set; }

        // RBAC user roles -> GroupMembers table
        public ICollection<GroupMemberEntity> UserRoles { get; set; } = new List<GroupMemberEntity>();
    }
}
