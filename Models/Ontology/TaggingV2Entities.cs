using System;

namespace Sciencetopia.Models.Ontology;

// Ontology V2 — faceted tag model (plan §3.2). Lives in the KnowledgeGraph schema
// alongside the legacy Tags/TagTypes/TypesOfTags, which are NOT touched. Additive only.

/// <summary>A tag dimension (topic, difficulty, language, resource_type, ...).</summary>
public class TagFacet
{
    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string? FacetType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A concrete tag value within a facet. Bridges to a legacy Tag via LegacyTagStableId.</summary>
public class TagValue
{
    public Guid Id { get; set; }
    public Guid StableId { get; set; }
    public Guid FacetId { get; set; }
    public string ValueType { get; set; } = TagValueTypes.Custom;
    public Guid? LegacyTagStableId { get; set; }  // -> KnowledgeGraph.Tags.StableId (no FK)
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Maps a TagValue to a Concept. Only exact/alias imply equivalence (no auto-merge for others).</summary>
public class TagConceptMapping
{
    public Guid Id { get; set; }
    public Guid TagValueId { get; set; }
    public Guid ConceptId { get; set; }
    public string MappingType { get; set; } = TagMappingTypes.Related;
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Generic assignment of a TagValue to a product entity (StudyPlan/Lesson/Resource/StudyGroup/ConceptPage).</summary>
public class TagAssignment
{
    public Guid Id { get; set; }
    public Guid TagValueId { get; set; }
    public string TargetType { get; set; } = "";   // see TagAssignmentTargetTypes
    public Guid TargetId { get; set; }
    public string? Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
