using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sciencetopia.Data;
using Xunit;

namespace SciencetopiaWebApplication.Tests;

/// <summary>
/// Phase 1 guardrail tests: the AddOntologyV2Schema migration must be ADDITIVE ONLY,
/// and the model must still contain every pre-existing table after the additions.
/// These run offline (no database connection).
/// </summary>
public class OntologyV2MigrationTests
{
    // The 16 tables Phase 1 introduces (must match ApplicationDbContext.OntologyV2.cs).
    private static readonly string[] NewTables =
    {
        "Concepts", "ConceptSchemes", "ConceptPlacements", "ConceptRelations", "ConceptPages",
        "ResourceConceptAssertions", "LessonConceptAssertions", "StudyPlanConceptAssertions",
        "StudyGroupConceptInterests", "TagFacets", "TagValues", "TagConceptMappings",
        "TagAssignments", "EntityL10nSets", "OntologyProposals", "TagMigrationRecords"
    };

    // A representative set of pre-existing tables (each mapped to an EF entity) that must
    // NOT be removed or altered. Note: some real tables (e.g. LessonTagAssignments) are
    // migration-only with no EF entity, so they don't appear in ctx.Model; their survival
    // is instead guaranteed by the operation-level tests below (Up has no DropTable, Down
    // drops only the 16 new tables).
    private static readonly string[] ExistingTables =
    {
        "Tags", "KnowledgeNodes", "Resources", "TagTypes", "TypesOfTags", "TagRepresentativeNode",
        "StudyPlans", "Lessons",
        "L10nSets", "L10nItems", "L10nSetItems", "NodeL10nSets", "TagL10nSets",
        "StudyGroups", "Groups", "UserGroups"
    };

    private static Migration GetMigration() =>
        new SciencetopiaWebApplication.Migrations.AddOntologyV2Schema();

    [Fact]
    public void Migration_Up_ContainsNoDestructiveOperations()
    {
        var up = GetMigration().UpOperations;

        Assert.DoesNotContain(up, o => o is DropTableOperation);
        Assert.DoesNotContain(up, o => o is DropColumnOperation);
        Assert.DoesNotContain(up, o => o is DropSchemaOperation);
        Assert.DoesNotContain(up, o => o is DropForeignKeyOperation);
        Assert.DoesNotContain(up, o => o is DropPrimaryKeyOperation);
        Assert.DoesNotContain(up, o => o is DropIndexOperation);
        Assert.DoesNotContain(up, o => o is DropCheckConstraintOperation);
        Assert.DoesNotContain(up, o => o is RenameTableOperation);
        Assert.DoesNotContain(up, o => o is RenameColumnOperation);
        Assert.DoesNotContain(up, o => o is AlterColumnOperation);

        // Only additive op kinds may appear in Up.
        foreach (var op in up)
        {
            Assert.True(
                op is CreateTableOperation or CreateIndexOperation or EnsureSchemaOperation
                   or AddForeignKeyOperation or AddPrimaryKeyOperation or AddUniqueConstraintOperation,
                $"Unexpected non-additive operation in Up: {op.GetType().Name}");
        }
    }

    [Fact]
    public void Migration_Up_CreatesExactlyTheNewOntologyTables_AndTouchesNoExisting()
    {
        var created = GetMigration().UpOperations
            .OfType<CreateTableOperation>()
            .Select(o => o.Name)
            .OrderBy(n => n)
            .ToArray();

        Assert.Equal(NewTables.OrderBy(n => n).ToArray(), created);

        // No created table collides with a pre-existing table name.
        foreach (var t in created)
            Assert.DoesNotContain(t, ExistingTables);
    }

    [Fact]
    public void Migration_Down_OnlyDropsTheNewTables()
    {
        var dropped = GetMigration().DownOperations
            .OfType<DropTableOperation>()
            .Select(o => o.Name)
            .ToArray();

        // Rollback must remove ONLY the Phase 1 tables, never a pre-existing one.
        foreach (var t in dropped)
        {
            Assert.Contains(t, NewTables);
            Assert.DoesNotContain(t, ExistingTables);
        }
    }

    [Fact]
    public void Model_RetainsExistingTables_AndAddsNewOntologyTables()
    {
        // Build the relational model under the SqlServer provider (no connection is opened
        // by reading ctx.Model) so default table names + relational conventions resolve.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ontology_phase1_modeltest;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;

        using var ctx = new ApplicationDbContext(options);
        var tableNames = ctx.Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(n => n != null)
            .ToHashSet();

        foreach (var t in ExistingTables)
            Assert.True(tableNames.Contains(t), $"Existing table missing from model: {t}");

        foreach (var t in NewTables)
            Assert.True(tableNames.Contains(t), $"New Ontology V2 table missing from model: {t}");
    }

    [Fact]
    public void NewTables_IdPrimaryKeys_UseNoStoreDefault_MatchingExistingConvention()
    {
        // Phase 1.1: Id PKs must have NO store default (NEWID/NEWSEQUENTIALID), matching the
        // existing Tags/KnowledgeNodes/StudyPlans/Lessons convention where the Id key is
        // EF-generated client-side (SequentialGuidValueGenerator). A NEWID() default would
        // both deviate from convention and fragment the clustered PK on these high-growth tables.
        foreach (var ct in GetMigration().UpOperations.OfType<CreateTableOperation>())
        {
            var idCol = ct.Columns.Single(c => c.Name == "Id");
            Assert.Equal("uniqueidentifier", idCol.ColumnType);
            Assert.Null(idCol.DefaultValueSql);
            Assert.NotNull(ct.PrimaryKey);
            Assert.Contains("Id", ct.PrimaryKey!.Columns);
        }
    }

    [Fact]
    public void NewTables_AreMappedToExpectedSchemas()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=localhost;Database=ontology_phase1_schematest;Trusted_Connection=True;TrustServerCertificate=True;")
            .Options;
        using var ctx = new ApplicationDbContext(options);

        string? SchemaOf(string table) => ctx.Model.GetEntityTypes()
            .FirstOrDefault(e => e.GetTableName() == table)?.GetSchema();

        Assert.Equal("Ontology", SchemaOf("Concepts"));
        Assert.Equal("Ontology", SchemaOf("LessonConceptAssertions"));
        Assert.Equal("Ontology", SchemaOf("OntologyProposals"));
        Assert.Equal("KnowledgeGraph", SchemaOf("TagFacets"));
        Assert.Equal("KnowledgeGraph", SchemaOf("TagValues"));
        Assert.Equal("L10n", SchemaOf("EntityL10nSets"));
    }
}
