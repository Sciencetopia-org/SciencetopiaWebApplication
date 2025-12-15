using System;
using System.ComponentModel.DataAnnotations;

namespace Sciencetopia.Models
{
    public class GroupPlanSwitch
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid StudyGroupId { get; set; }
        public Guid PlanStableId { get; set; }
        public Guid? FromVersionId { get; set; }
        public int? FromVersionNumber { get; set; }
        public Guid ToVersionId { get; set; }
        public int ToVersionNumber { get; set; }
        public DateTimeOffset EffectiveAt { get; set; }
        public DateTimeOffset? ExecutedAt { get; set; }
        public string? Actor { get; set; }
        public string? Reason { get; set; }
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}

