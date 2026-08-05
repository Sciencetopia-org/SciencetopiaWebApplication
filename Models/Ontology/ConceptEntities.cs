using System;

namespace Sciencetopia.Models.Ontology;

// Ontology V2 — concept core (plan §3.1). Language-neutral identity; display names
// come from L10n (EntityL10nSets), never stored as identity here. All tables additive;
// no behavior is wired in Phase 1.

/// <summary>A knowledge organization scheme (default ontology, curriculum view, etc.).</summary>
public class ConceptScheme
{
    public Guid Id { get; set; }
    public Guid StableId { get; set; }
    public string Key { get; set; } = "";
    public string? SchemeType { get; set; }
    public int Version { get; set; } = 1;
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A language-neutral knowledge concept identity. No Level here (level lives in ConceptPlacement).</summary>
public class Concept
{
    public Guid Id { get; set; }
    public Guid StableId { get; set; }
    public string Status { get; set; } = ConceptStatuses.Draft;
    public Guid? MergedIntoConceptId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A Concept's position (level + parent) inside a ConceptScheme. Level belongs here.</summary>
public class ConceptPlacement
{
    public Guid Id { get; set; }
    public Guid ConceptId { get; set; }
    public Guid SchemeId { get; set; }
    public string Level { get; set; } = ConceptLevels.Topic;
    public Guid? ParentConceptId { get; set; }
    public int OrderIndex { get; set; }
    public int DisplayPriority { get; set; }
    public string? Source { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public double? Confidence { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A typed semantic relation between two Concepts. One canonical direction only.</summary>
public class ConceptRelation
{
    public Guid Id { get; set; }
    public Guid FromConceptId { get; set; }
    public Guid ToConceptId { get; set; }
    public string RelationType { get; set; } = ConceptRelationTypes.RelatedTo;
    public string? Reason { get; set; }       // required for AI-suggested related_to
    public double? Confidence { get; set; }
    public string? Source { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>An internal concept overview / anchor / visual node for one Concept. Not a Resource.</summary>
public class ConceptPage
{
    public Guid Id { get; set; }
    public Guid StableId { get; set; }
    public Guid ConceptId { get; set; }
    public Guid? SchemeId { get; set; }
    public string PageType { get; set; } = ConceptPageTypes.ConceptOverview;
    public string ContentStatus { get; set; } = "Draft";
    public bool IsRenderable { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
