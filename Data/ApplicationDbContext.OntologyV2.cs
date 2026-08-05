using Microsoft.EntityFrameworkCore;
using Sciencetopia.Models.Ontology;

namespace Sciencetopia.Data
{
    // Ontology V2 — Phase 1 additive schema. Defined in a partial class so the large
    // ApplicationDbContext.cs is touched only minimally (one `partial` keyword + one
    // call from OnModelCreating). Every mapping here CREATES a new table; none alters,
    // drops, or renames an existing table/column. No FK targets an existing table.
    public partial class ApplicationDbContext
    {
        // ---- Concept core (schema: Ontology) ----
        public DbSet<ConceptScheme> ConceptSchemes => Set<ConceptScheme>();
        public DbSet<Concept> Concepts => Set<Concept>();
        public DbSet<ConceptPlacement> ConceptPlacements => Set<ConceptPlacement>();
        public DbSet<ConceptRelation> ConceptRelations => Set<ConceptRelation>();
        public DbSet<ConceptPage> ConceptPages => Set<ConceptPage>();

        // ---- Semantic assertions (schema: Ontology) ----
        public DbSet<ResourceConceptAssertion> ResourceConceptAssertions => Set<ResourceConceptAssertion>();
        public DbSet<LessonConceptAssertion> LessonConceptAssertions => Set<LessonConceptAssertion>();
        public DbSet<StudyPlanConceptAssertion> StudyPlanConceptAssertions => Set<StudyPlanConceptAssertion>();
        public DbSet<StudyGroupConceptInterest> StudyGroupConceptInterests => Set<StudyGroupConceptInterest>();

        // ---- Faceted tags (schema: KnowledgeGraph) ----
        public DbSet<TagFacet> TagFacets => Set<TagFacet>();
        public DbSet<TagValue> TagValues => Set<TagValue>();
        public DbSet<TagConceptMapping> TagConceptMappings => Set<TagConceptMapping>();
        public DbSet<TagAssignment> TagAssignments => Set<TagAssignment>();

        // ---- Governance + generalized L10n binding ----
        public DbSet<EntityL10nSet> EntityL10nSets => Set<EntityL10nSet>();          // schema: L10n
        public DbSet<OntologyProposal> OntologyProposals => Set<OntologyProposal>(); // schema: Ontology
        public DbSet<TagMigrationRecord> TagMigrationRecords => Set<TagMigrationRecord>(); // schema: Ontology

        private const string OntologySchema = "Ontology";
        private const string KnowledgeGraphSchema = "KnowledgeGraph";
        private const string L10nSchema = "L10n";
        private const string Utc = "SYSUTCDATETIME()";
        private const string NewId = "NEWID()";

        private static void OntologyTimestamps<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> eb,
            bool withUpdated = true) where T : class
        {
            eb.Property("CreatedAt").HasDefaultValueSql(Utc);
            if (withUpdated) eb.Property("UpdatedAt").HasDefaultValueSql(Utc);
        }

        /// <summary>Additive Ontology V2 mappings. Called from OnModelCreating after existing config.</summary>
        private void ConfigureOntologyV2(ModelBuilder b)
        {
            // ---------------- Concept core ----------------
            b.Entity<ConceptScheme>(eb =>
            {
                eb.ToTable("ConceptSchemes", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.StableId).HasDefaultValueSql(NewId);
                eb.Property(x => x.Key).HasMaxLength(100).IsRequired();
                eb.Property(x => x.SchemeType).HasMaxLength(50);
                eb.Property(x => x.Version).HasDefaultValue(1);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StableId).IsUnique().HasDatabaseName("UX_ConceptSchemes_StableId");
                eb.HasIndex(x => x.Key).HasDatabaseName("IX_ConceptSchemes_Key");
                eb.HasIndex(x => x.IsDefault).HasDatabaseName("IX_ConceptSchemes_IsDefault");
            });

            b.Entity<Concept>(eb =>
            {
                eb.ToTable("Concepts", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.StableId).HasDefaultValueSql(NewId);
                eb.Property(x => x.Status).HasMaxLength(32).IsRequired().HasDefaultValue(ConceptStatuses.Draft);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StableId).IsUnique().HasDatabaseName("UX_Concepts_StableId");
                eb.HasIndex(x => x.Status).HasDatabaseName("IX_Concepts_Status");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.MergedIntoConceptId)
                  .OnDelete(DeleteBehavior.NoAction);
            });

            b.Entity<ConceptPlacement>(eb =>
            {
                eb.ToTable("ConceptPlacements", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Level).HasMaxLength(32).IsRequired();
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => new { x.SchemeId, x.ConceptId }).IsUnique().HasDatabaseName("UX_ConceptPlacements_Scheme_Concept");
                eb.HasIndex(x => x.ParentConceptId).HasDatabaseName("IX_ConceptPlacements_Parent");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<ConceptScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ParentConceptId).OnDelete(DeleteBehavior.NoAction);
            });

            b.Entity<ConceptRelation>(eb =>
            {
                eb.ToTable("ConceptRelations", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.RelationType).HasMaxLength(32).IsRequired();
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.Reason).HasColumnType("nvarchar(max)");
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => new { x.FromConceptId, x.ToConceptId, x.RelationType }).IsUnique()
                  .HasDatabaseName("UX_ConceptRelations_From_To_Type");
                eb.HasIndex(x => x.ToConceptId).HasDatabaseName("IX_ConceptRelations_To");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.FromConceptId).OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ToConceptId).OnDelete(DeleteBehavior.NoAction);
            });

            b.Entity<ConceptPage>(eb =>
            {
                eb.ToTable("ConceptPages", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.StableId).HasDefaultValueSql(NewId);
                eb.Property(x => x.PageType).HasMaxLength(32).IsRequired();
                eb.Property(x => x.ContentStatus).HasMaxLength(32).IsRequired().HasDefaultValue("Draft");
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StableId).IsUnique().HasDatabaseName("UX_ConceptPages_StableId");
                eb.HasIndex(x => x.ConceptId).HasDatabaseName("IX_ConceptPages_Concept");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<ConceptScheme>().WithMany().HasForeignKey(x => x.SchemeId).OnDelete(DeleteBehavior.NoAction);
            });

            // ---------------- Assertions ----------------
            b.Entity<ResourceConceptAssertion>(eb =>
            {
                eb.ToTable("ResourceConceptAssertions", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Role).HasMaxLength(32);
                eb.Property(x => x.Coverage).HasMaxLength(16);
                eb.Property(x => x.LevelRelation).HasMaxLength(32);
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.ResourceId).HasDatabaseName("IX_ResourceConceptAssertions_Resource");
                eb.HasIndex(x => new { x.ConceptId, x.ResourceId }).HasDatabaseName("IX_ResourceConceptAssertions_Concept_Resource");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<LessonConceptAssertion>(eb =>
            {
                eb.ToTable("LessonConceptAssertions", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Role).HasMaxLength(32);
                eb.Property(x => x.Importance).HasMaxLength(16);
                eb.Property(x => x.ExpectedMastery).HasMaxLength(16);
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => new { x.LessonStableId, x.LessonVersionNumber }).HasDatabaseName("IX_LessonConceptAssertions_Lesson");
                eb.HasIndex(x => x.ConceptId).HasDatabaseName("IX_LessonConceptAssertions_Concept");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<StudyPlanConceptAssertion>(eb =>
            {
                eb.ToTable("StudyPlanConceptAssertions", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Role).HasMaxLength(32);
                eb.Property(x => x.Importance).HasMaxLength(16);
                eb.Property(x => x.TargetMastery).HasMaxLength(16);
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StudyPlanStableId).HasDatabaseName("IX_StudyPlanConceptAssertions_Plan");
                eb.HasIndex(x => x.ConceptId).HasDatabaseName("IX_StudyPlanConceptAssertions_Concept");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<StudyGroupConceptInterest>(eb =>
            {
                eb.ToTable("StudyGroupConceptInterests", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Role).HasMaxLength(32);
                eb.Property(x => x.Intensity).HasMaxLength(16);
                eb.Property(x => x.Source).HasMaxLength(50);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StudyGroupId).HasDatabaseName("IX_StudyGroupConceptInterests_Group");
                eb.HasIndex(x => x.ConceptId).HasDatabaseName("IX_StudyGroupConceptInterests_Concept");
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------- Faceted tags (KnowledgeGraph schema) ----------------
            b.Entity<TagFacet>(eb =>
            {
                eb.ToTable("TagFacets", KnowledgeGraphSchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.Key).HasMaxLength(50).IsRequired();
                eb.Property(x => x.FacetType).HasMaxLength(50);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.Key).IsUnique().HasDatabaseName("UX_TagFacets_Key");
            });

            b.Entity<TagValue>(eb =>
            {
                eb.ToTable("TagValues", KnowledgeGraphSchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.StableId).HasDefaultValueSql(NewId);
                eb.Property(x => x.ValueType).HasMaxLength(32).IsRequired().HasDefaultValue(TagValueTypes.Custom);
                OntologyTimestamps(eb);
                eb.HasIndex(x => x.StableId).IsUnique().HasDatabaseName("UX_TagValues_StableId");
                eb.HasIndex(x => x.FacetId).HasDatabaseName("IX_TagValues_Facet");
                eb.HasIndex(x => x.LegacyTagStableId).HasDatabaseName("IX_TagValues_LegacyTag");
                eb.HasOne<TagFacet>().WithMany().HasForeignKey(x => x.FacetId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TagConceptMapping>(eb =>
            {
                eb.ToTable("TagConceptMappings", KnowledgeGraphSchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.MappingType).HasMaxLength(16).IsRequired();
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb);
                eb.HasIndex(x => new { x.TagValueId, x.ConceptId, x.MappingType }).IsUnique()
                  .HasDatabaseName("UX_TagConceptMappings_Value_Concept_Type");
                eb.HasIndex(x => x.ConceptId).HasDatabaseName("IX_TagConceptMappings_Concept");
                eb.HasOne<TagValue>().WithMany().HasForeignKey(x => x.TagValueId).OnDelete(DeleteBehavior.Restrict);
                eb.HasOne<Concept>().WithMany().HasForeignKey(x => x.ConceptId).OnDelete(DeleteBehavior.Restrict);
            });

            b.Entity<TagAssignment>(eb =>
            {
                eb.ToTable("TagAssignments", KnowledgeGraphSchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.TargetType).HasMaxLength(32).IsRequired();
                eb.Property(x => x.Role).HasMaxLength(32);
                eb.Property(x => x.CreatedAt).HasDefaultValueSql(Utc);
                eb.HasIndex(x => new { x.TargetType, x.TargetId }).HasDatabaseName("IX_TagAssignments_Target");
                eb.HasIndex(x => x.TagValueId).HasDatabaseName("IX_TagAssignments_Value");
                eb.HasOne<TagValue>().WithMany().HasForeignKey(x => x.TagValueId).OnDelete(DeleteBehavior.Restrict);
            });

            // ---------------- Generalized L10n binding (L10n schema) ----------------
            b.Entity<EntityL10nSet>(eb =>
            {
                eb.ToTable("EntityL10nSets", L10nSchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.EntityType).HasMaxLength(32).IsRequired();
                eb.Property(x => x.Purpose).HasMaxLength(32).IsRequired().HasDefaultValue(EntityL10nPurposes.Label);
                eb.Property(x => x.CreatedAt).HasDefaultValueSql(Utc);
                eb.HasIndex(x => new { x.EntityType, x.EntityStableId, x.Purpose }).HasDatabaseName("IX_EntityL10nSets_Entity_Purpose");
                eb.HasIndex(x => x.L10nSetId).HasDatabaseName("IX_EntityL10nSets_Set");
                // NOTE: no FK to L10n.L10nSets in Phase 1 (kept uncoupled; Phase 2 may add it).
            });

            // ---------------- Governance (Ontology schema) ----------------
            b.Entity<OntologyProposal>(eb =>
            {
                eb.ToTable("OntologyProposals", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.ProposalType).HasMaxLength(50).IsRequired();
                eb.Property(x => x.Status).HasMaxLength(16).IsRequired().HasDefaultValue(ProposalStatuses.Pending);
                eb.Property(x => x.CreatedBy).HasMaxLength(128);
                eb.Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
                eb.Property(x => x.Reason).HasColumnType("nvarchar(max)");
                OntologyTimestamps(eb);
                eb.HasIndex(x => new { x.ProposalType, x.Status }).HasDatabaseName("IX_OntologyProposals_Type_Status");
            });

            b.Entity<TagMigrationRecord>(eb =>
            {
                eb.ToTable("TagMigrationRecords", OntologySchema);
                eb.HasKey(x => x.Id);
                eb.Property(x => x.OldName).HasMaxLength(400);
                eb.Property(x => x.Action).HasMaxLength(32);
                eb.Property(x => x.ReviewStatus).HasMaxLength(32).IsRequired().HasDefaultValue(OntologyReviewStatuses.Pending);
                OntologyTimestamps(eb, withUpdated: false);
                eb.HasIndex(x => x.OldStableId).HasDatabaseName("IX_TagMigrationRecords_OldStableId");
                eb.HasIndex(x => x.ReviewStatus).HasDatabaseName("IX_TagMigrationRecords_ReviewStatus");
            });
        }
    }
}
