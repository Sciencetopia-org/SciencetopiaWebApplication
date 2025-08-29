// Models/Plans/StudyPlanVersion.cs
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sciencetopia.Models
{
    public class StudyPlanVersion
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public Guid StudyPlanId { get; set; }

        [Required]
        public int VersionNumber { get; set; } // 1,2,3...

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
