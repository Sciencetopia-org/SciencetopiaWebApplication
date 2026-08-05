using System;

namespace Sciencetopia.Models.Ontology;

// Ontology V2 — governance + generalized L10n binding (plan §3.4, §3.5).
// Additive only. EntityL10nSet generalizes NodeL10nSets/TagL10nSets WITHOUT replacing
// them (those remain the live path until Phase 2 wires a read shim).

/// <summary>
/// Generalized binding of any localizable entity to an L10nSet. Coexists with the
/// per-entity NodeL10nSets/TagL10nSets. L10nSetId references L10n.L10nSets but has NO
/// FK in Phase 1 (kept uncoupled; Phase 2 may add it with the read shim).
///
/// BINDING KEY = <see cref="EntityStableId"/>: the cross-language, cross-version SEMANTIC
/// identity (StableId) of the target entity. NEVER store a row/version Id here — see plan
/// §12 (StableId clarification) and §13.x (Phase 2.1). (Id below is this row's own PK.)
/// </summary>
public class EntityL10nSet
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = "";   // see EntityL10nTypes
    public Guid EntityStableId { get; set; }        // semantic StableId of the target — NOT a row/version Id
    public string Purpose { get; set; } = EntityL10nPurposes.Label;
    public Guid L10nSetId { get; set; }            // -> L10n.L10nSets.L10nSetId (no FK in Phase 1)
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// AI / automation proposal. Automation NEVER mutates canonical ontology directly
/// (brief §10); it writes a proposal here. Canonical change happens only after approval.
/// </summary>
public class OntologyProposal
{
    public Guid Id { get; set; }
    public string ProposalType { get; set; } = "";
    public string? PayloadJson { get; set; }
    public string Status { get; set; } = ProposalStatuses.Pending;
    public string? CreatedBy { get; set; }
    public double? Confidence { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Audit trail for legacy-tag migration decisions (plan §3.5; brief §9).
/// (Plan name: TagMigrationRecords — the brief's "LegacyTagMigrations" maps here.)
/// </summary>
public class TagMigrationRecord
{
    public Guid Id { get; set; }
    public Guid? OldTagId { get; set; }
    public Guid? OldStableId { get; set; }
    public string? OldName { get; set; }
    public Guid? NewTagValueId { get; set; }
    public Guid? NewConceptId { get; set; }
    public string? Action { get; set; }
    public double? Confidence { get; set; }
    public string ReviewStatus { get; set; } = OntologyReviewStatuses.Pending;
    public DateTimeOffset? AppliedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
