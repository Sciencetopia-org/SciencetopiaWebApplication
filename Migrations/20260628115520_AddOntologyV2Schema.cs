using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddOntologyV2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Ontology");

            migrationBuilder.CreateTable(
                name: "Concepts",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    MergedIntoConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Concepts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Concepts_Concepts_MergedIntoConceptId",
                        column: x => x.MergedIntoConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ConceptSchemes",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SchemeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptSchemes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EntityL10nSets",
                schema: "L10n",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EntityStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "label"),
                    L10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntityL10nSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OntologyProposals",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProposalType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "pending"),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OntologyProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagFacets",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FacetType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagFacets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagMigrationRecords",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldTagId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OldStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OldName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    NewTagValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    NewConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagMigrationRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConceptRelations",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelationType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptRelations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptRelations_Concepts_FromConceptId",
                        column: x => x.FromConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptRelations_Concepts_ToConceptId",
                        column: x => x.ToConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "LessonConceptAssertions",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonVersionNumber = table.Column<int>(type: "int", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Importance = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    ExpectedMastery = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonConceptAssertions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonConceptAssertions_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ResourceConceptAssertions",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Coverage = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    LevelRelation = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false),
                    Weight = table.Column<double>(type: "float", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceConceptAssertions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceConceptAssertions_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudyGroupConceptInterests",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Intensity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroupConceptInterests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyGroupConceptInterests_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanConceptAssertions",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Importance = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    TargetMastery = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanConceptAssertions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPlanConceptAssertions_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptPages",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PageType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Draft"),
                    IsRenderable = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptPages_ConceptSchemes_SchemeId",
                        column: x => x.SchemeId,
                        principalSchema: "Ontology",
                        principalTable: "ConceptSchemes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ConceptPages_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConceptPlacements",
                schema: "Ontology",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Level = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ParentConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OrderIndex = table.Column<int>(type: "int", nullable: false),
                    DisplayPriority = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConceptPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConceptPlacements_ConceptSchemes_SchemeId",
                        column: x => x.SchemeId,
                        principalSchema: "Ontology",
                        principalTable: "ConceptSchemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptPlacements_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConceptPlacements_Concepts_ParentConceptId",
                        column: x => x.ParentConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TagValues",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    FacetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ValueType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "custom"),
                    LegacyTagStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagValues_TagFacets_FacetId",
                        column: x => x.FacetId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "TagFacets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TagAssignments",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagAssignments_TagValues_TagValueId",
                        column: x => x.TagValueId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "TagValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TagConceptMappings",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagValueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MappingType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    ReviewStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "Pending"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagConceptMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagConceptMappings_Concepts_ConceptId",
                        column: x => x.ConceptId,
                        principalSchema: "Ontology",
                        principalTable: "Concepts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TagConceptMappings_TagValues_TagValueId",
                        column: x => x.TagValueId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "TagValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConceptPages_Concept",
                schema: "Ontology",
                table: "ConceptPages",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptPages_SchemeId",
                schema: "Ontology",
                table: "ConceptPages",
                column: "SchemeId");

            migrationBuilder.CreateIndex(
                name: "UX_ConceptPages_StableId",
                schema: "Ontology",
                table: "ConceptPages",
                column: "StableId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptPlacements_ConceptId",
                schema: "Ontology",
                table: "ConceptPlacements",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptPlacements_Parent",
                schema: "Ontology",
                table: "ConceptPlacements",
                column: "ParentConceptId");

            migrationBuilder.CreateIndex(
                name: "UX_ConceptPlacements_Scheme_Concept",
                schema: "Ontology",
                table: "ConceptPlacements",
                columns: new[] { "SchemeId", "ConceptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptRelations_To",
                schema: "Ontology",
                table: "ConceptRelations",
                column: "ToConceptId");

            migrationBuilder.CreateIndex(
                name: "UX_ConceptRelations_From_To_Type",
                schema: "Ontology",
                table: "ConceptRelations",
                columns: new[] { "FromConceptId", "ToConceptId", "RelationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Concepts_MergedIntoConceptId",
                schema: "Ontology",
                table: "Concepts",
                column: "MergedIntoConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_Concepts_Status",
                schema: "Ontology",
                table: "Concepts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "UX_Concepts_StableId",
                schema: "Ontology",
                table: "Concepts",
                column: "StableId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConceptSchemes_IsDefault",
                schema: "Ontology",
                table: "ConceptSchemes",
                column: "IsDefault");

            migrationBuilder.CreateIndex(
                name: "IX_ConceptSchemes_Key",
                schema: "Ontology",
                table: "ConceptSchemes",
                column: "Key");

            migrationBuilder.CreateIndex(
                name: "UX_ConceptSchemes_StableId",
                schema: "Ontology",
                table: "ConceptSchemes",
                column: "StableId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EntityL10nSets_Entity_Purpose",
                schema: "L10n",
                table: "EntityL10nSets",
                columns: new[] { "EntityType", "EntityStableId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_EntityL10nSets_Set",
                schema: "L10n",
                table: "EntityL10nSets",
                column: "L10nSetId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonConceptAssertions_Concept",
                schema: "Ontology",
                table: "LessonConceptAssertions",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonConceptAssertions_Lesson",
                schema: "Ontology",
                table: "LessonConceptAssertions",
                columns: new[] { "LessonStableId", "LessonVersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_OntologyProposals_Type_Status",
                schema: "Ontology",
                table: "OntologyProposals",
                columns: new[] { "ProposalType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConceptAssertions_Concept_Resource",
                schema: "Ontology",
                table: "ResourceConceptAssertions",
                columns: new[] { "ConceptId", "ResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceConceptAssertions_Resource",
                schema: "Ontology",
                table: "ResourceConceptAssertions",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupConceptInterests_Concept",
                schema: "Ontology",
                table: "StudyGroupConceptInterests",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupConceptInterests_Group",
                schema: "Ontology",
                table: "StudyGroupConceptInterests",
                column: "StudyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanConceptAssertions_Concept",
                schema: "Ontology",
                table: "StudyPlanConceptAssertions",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanConceptAssertions_Plan",
                schema: "Ontology",
                table: "StudyPlanConceptAssertions",
                column: "StudyPlanStableId");

            migrationBuilder.CreateIndex(
                name: "IX_TagAssignments_Target",
                schema: "KnowledgeGraph",
                table: "TagAssignments",
                columns: new[] { "TargetType", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_TagAssignments_Value",
                schema: "KnowledgeGraph",
                table: "TagAssignments",
                column: "TagValueId");

            migrationBuilder.CreateIndex(
                name: "IX_TagConceptMappings_Concept",
                schema: "KnowledgeGraph",
                table: "TagConceptMappings",
                column: "ConceptId");

            migrationBuilder.CreateIndex(
                name: "UX_TagConceptMappings_Value_Concept_Type",
                schema: "KnowledgeGraph",
                table: "TagConceptMappings",
                columns: new[] { "TagValueId", "ConceptId", "MappingType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_TagFacets_Key",
                schema: "KnowledgeGraph",
                table: "TagFacets",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagMigrationRecords_OldStableId",
                schema: "Ontology",
                table: "TagMigrationRecords",
                column: "OldStableId");

            migrationBuilder.CreateIndex(
                name: "IX_TagMigrationRecords_ReviewStatus",
                schema: "Ontology",
                table: "TagMigrationRecords",
                column: "ReviewStatus");

            migrationBuilder.CreateIndex(
                name: "IX_TagValues_Facet",
                schema: "KnowledgeGraph",
                table: "TagValues",
                column: "FacetId");

            migrationBuilder.CreateIndex(
                name: "IX_TagValues_LegacyTag",
                schema: "KnowledgeGraph",
                table: "TagValues",
                column: "LegacyTagStableId");

            migrationBuilder.CreateIndex(
                name: "UX_TagValues_StableId",
                schema: "KnowledgeGraph",
                table: "TagValues",
                column: "StableId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConceptPages",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "ConceptPlacements",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "ConceptRelations",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "EntityL10nSets",
                schema: "L10n");

            migrationBuilder.DropTable(
                name: "LessonConceptAssertions",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "OntologyProposals",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "ResourceConceptAssertions",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "StudyGroupConceptInterests",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "StudyPlanConceptAssertions",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "TagAssignments",
                schema: "KnowledgeGraph");

            migrationBuilder.DropTable(
                name: "TagConceptMappings",
                schema: "KnowledgeGraph");

            migrationBuilder.DropTable(
                name: "TagMigrationRecords",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "ConceptSchemes",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "Concepts",
                schema: "Ontology");

            migrationBuilder.DropTable(
                name: "TagValues",
                schema: "KnowledgeGraph");

            migrationBuilder.DropTable(
                name: "TagFacets",
                schema: "KnowledgeGraph");
        }
    }
}
