// Models/Plans/LessonDraft.cs
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class LessonDraft
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid LessonId { get; set; }

        [Required]
        public int DraftNumber { get; set; }

        [MaxLength(256)]
        public string? Title { get; set; }

        public string? Description { get; set; }

        [Required]
        public string SnapshotJson { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? ChangeNotes { get; set; }

        public DraftStatus? DraftStatus { get; set; } = null;

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [MaxLength(128)]
        public string? UpdatedBy { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; }

        [Required]
        public DateTime UpdatedDate { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
