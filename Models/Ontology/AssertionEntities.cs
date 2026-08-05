using System;

namespace Sciencetopia.Models.Ontology;

// Ontology V2 — semantic assertion records (plan §3.3). Schema created in Phase 1
// (additive, empty); populated in Phase 6. Target ids (ResourceId/StudyGroupId/
// LessonStableId/StudyPlanStableId) are plain Guids — NO FK to legacy tables, to keep
// this strictly additive and uncoupled. Only ConceptId is a real FK (intra-new-schema).

/// <summary>How a Resource (external material) covers a Concept.</summary>
public class ResourceConceptAssertion
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }   // -> KnowledgeGraph.Resources.Id (no FK)
    public Guid ConceptId { get; set; }
    public string? Role { get; set; }       // primary_topic, covers, context, prerequisite, ...
    public string? Coverage { get; set; }   // full, partial, brief, mention
    public string? LevelRelation { get; set; }
    public bool IsPrimary { get; set; }
    public double? Weight { get; set; }
    public string? Source { get; set; }
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>How a Lesson (StudyPlan unit) relates to a Concept. Keyed like LessonTagAssignments.</summary>
public class LessonConceptAssertion
{
    public Guid Id { get; set; }
    public Guid LessonStableId { get; set; }   // -> StudyPlans.Lessons.StableId (no FK)
    public int LessonVersionNumber { get; set; }
    public Guid ConceptId { get; set; }
    public string? Role { get; set; }           // learning_goal, primary_topic, prerequisite, ...
    public string? Importance { get; set; }
    public string? ExpectedMastery { get; set; }
    public int OrderIndex { get; set; }
    public string? Source { get; set; }
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>How a StudyPlan relates to a Concept.</summary>
public class StudyPlanConceptAssertion
{
    public Guid Id { get; set; }
    public Guid StudyPlanStableId { get; set; } // -> StudyPlans.StudyPlans.StableId (no FK)
    public Guid ConceptId { get; set; }
    public string? Role { get; set; }            // main_goal, covered_topic, prerequisite, extension
    public string? Importance { get; set; }
    public string? TargetMastery { get; set; }
    public string? Source { get; set; }
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A StudyGroup's interest/focus on a Concept.</summary>
public class StudyGroupConceptInterest
{
    public Guid Id { get; set; }
    public Guid StudyGroupId { get; set; }       // -> StudyGroups.StudyGroups.Id (no FK)
    public Guid ConceptId { get; set; }
    public string? Role { get; set; }            // main_topic, secondary_topic, prerequisite_expected, future_topic
    public string? Intensity { get; set; }
    public string? Source { get; set; }
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
