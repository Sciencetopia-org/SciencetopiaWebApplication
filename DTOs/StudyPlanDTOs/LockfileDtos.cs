using System;
using System.Collections.Generic;

namespace Sciencetopia.DTOs
{
    public class StudyPlanLockfile
    {
        public List<StudyPlanLockfileItem> Lessons { get; set; } = new();
    }

    public class StudyPlanLockfileItem
    {
        public Guid LessonStableId { get; set; }
        public int LessonVersionNumber { get; set; }
        public string StepType { get; set; } = string.Empty; // Prerequisite|Main|AdvancedTopic
        public int Index { get; set; }
        public bool Breaking { get; set; } = false; // optional, future use
    }

    public class LockfileDiff
    {
        public LockfileDiffSummary Summary { get; set; } = new();
        public List<LockfileChange> Changes { get; set; } = new();
    }

    public class LockfileDiffSummary
    {
        public int Added { get; set; }
        public int Removed { get; set; }
        public int Upgraded { get; set; }
        public int Downgraded { get; set; }
        public int Reordered { get; set; }
    }

    public class LockfileChange
    {
        public string Type { get; set; } = string.Empty; // added|removed|upgraded|downgraded|reordered
        public Guid LessonStableId { get; set; }
        public int? FromVersionNumber { get; set; }
        public int? ToVersionNumber { get; set; }
        public int? FromIndex { get; set; }
        public int? ToIndex { get; set; }
        public bool Breaking { get; set; } = false;
    }
}

