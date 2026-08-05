namespace Sciencetopia.Models.Ontology;

// Ontology V2 — language-neutral string constants for Status/Level/Type/Role columns.
// Stored as strings (matching the codebase's string Status/Kind convention and the
// Neo4j TagLevel.name literals) rather than enums. Phase 1: definitions only; nothing
// reads these yet.

public static class ConceptStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Merged = "Merged";
    public const string Deprecated = "Deprecated";
}

public static class ConceptLevels
{
    // Reuse the exact Neo4j TagLevel.name literals (audit §3).
    public const string Discipline = "Discipline";
    public const string Subject = "Subject";
    public const string Field = "Field";
    public const string Topic = "Topic";
    public const string Keyword = "Keyword";

    public static readonly string[] All = { Discipline, Subject, Field, Topic, Keyword };
}

public static class ConceptRelationTypes
{
    public const string BroaderThan = "broader_than";
    public const string PartOf = "part_of";
    public const string PrerequisiteOf = "prerequisite_of";
    public const string RelatedTo = "related_to";
    public const string ContrastsWith = "contrasts_with";
    public const string AnalogousTo = "analogous_to";
    public const string MethodFor = "method_for";
    public const string ApplicationOf = "application_of";

    public static readonly string[] All =
        { BroaderThan, PartOf, PrerequisiteOf, RelatedTo, ContrastsWith, AnalogousTo, MethodFor, ApplicationOf };
}

public static class ConceptPageTypes
{
    public const string ConceptOverview = "concept_overview";
    public const string IndexPage = "index_page";
    public const string SyntheticAnchor = "synthetic_anchor";
}

public static class TagValueTypes
{
    public const string ConceptBacked = "concept_backed";
    public const string Metadata = "metadata";
    public const string Pedagogical = "pedagogical";
    public const string Community = "community";
    public const string Workflow = "workflow";
    public const string Legacy = "legacy";
    public const string Custom = "custom";
}

public static class TagMappingTypes
{
    public const string Exact = "exact";
    public const string Alias = "alias";
    public const string Broader = "broader";
    public const string Narrower = "narrower";
    public const string Related = "related";

    // Only these two imply semantic equivalence (plan §6.3 / brief §6.3).
    public static readonly string[] EquivalenceTypes = { Exact, Alias };
}

public static class TagAssignmentTargetTypes
{
    public const string StudyPlan = "StudyPlan";
    public const string Lesson = "Lesson";
    public const string Resource = "Resource";
    public const string StudyGroup = "StudyGroup";
    public const string ConceptPage = "ConceptPage";
}

// AI proposal lifecycle (brief §10). Canonical changes only after Approved/Applied.
public static class ProposalStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Applied = "applied";
}

// Review status for assertions/placements/mappings. Mirrors the existing
// Sciencetopia.Models.ReviewStatus enum names, stored as strings here for
// Neo4j-friendliness and consistency with string Status columns elsewhere.
public static class OntologyReviewStatuses
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public static class EntityL10nPurposes
{
    public const string Label = "label";
    public const string Alias = "alias";
    public const string Description = "description";
    public const string Title = "title";
    public const string Summary = "summary";
    public const string Overview = "overview";
    public const string Instruction = "instruction";
    public const string UiDisplay = "ui_display";
    public const string Legacy = "legacy";
}

public static class EntityL10nTypes
{
    public const string Concept = "Concept";
    public const string ConceptPage = "ConceptPage";
    public const string ConceptScheme = "ConceptScheme";
    public const string TagFacet = "TagFacet";
    public const string TagValue = "TagValue";
    public const string Resource = "Resource";
    public const string StudyPlan = "StudyPlan";
    public const string Lesson = "Lesson";
    public const string StudyGroup = "StudyGroup";
}
