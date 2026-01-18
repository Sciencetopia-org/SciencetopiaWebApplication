using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    public partial class GroupPlanSwitchesAndBackfillStudyGroups : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupPlanSwitches",
                schema: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromVersionNumber = table.Column<int>(type: "int", nullable: true),
                    ToVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToVersionNumber = table.Column<int>(type: "int", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPlanSwitches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupPlanSwitches_GroupPlan_Effective",
                schema: "StudyGroups",
                table: "GroupPlanSwitches",
                columns: new[] { "StudyGroupId", "PlanStableId", "EffectiveAt" });

            // Backfill PlanVersionId from pinned/current
            migrationBuilder.Sql(@"
UPDATE gp
SET gp.PlanVersionId = sp.Id
FROM [StudyGroups].[StudyGroupStudyPlans] gp
JOIN [StudyPlans].[StudyPlans] sp ON sp.StableId = gp.StudyPlanStableId
WHERE (
  (gp.PinnedVersionNumber IS NOT NULL AND sp.VersionNumber = gp.PinnedVersionNumber)
  OR (gp.PinnedVersionNumber IS NULL AND sp.IsCurrent = 1)
)
AND (gp.PlanVersionId IS NULL);

-- Ensure ResolveToHead default false
UPDATE gp SET ResolveToHead = 0 WHERE ResolveToHead IS NULL;

            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupPlanSwitches",
                schema: "StudyGroups");
        }
    }
}

