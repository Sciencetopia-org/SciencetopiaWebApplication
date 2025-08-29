// Models/Plans/LessonVersion.cs
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class LessonVersion
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public Guid LessonId { get; set; }

        [Required]
        public int VersionNumber { get; set; }

        [MaxLength(256)]
        public string? Title { get; set; }

        public string? Description { get; set; }

        [Required]
        public string SnapshotJson { get; set; } = string.Empty;

        [MaxLength(1024)]
        public string? ChangeNotes { get; set; }

        [MaxLength(128)]
        public string? CreatedBy { get; set; }

        [Required]
        public DateTime CreatedDate { get; set; }

        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }
}
