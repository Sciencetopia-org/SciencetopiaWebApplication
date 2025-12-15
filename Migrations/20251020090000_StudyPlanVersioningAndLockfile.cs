using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    public partial class StudyPlanVersioningAndLockfile : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add LockfileJson to StudyPlans
            migrationBuilder.AddColumn<string>(
                name: "LockfileJson",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(max)",
                nullable: true);

            // Add PlanVersionId and ResolveToHead to StudyGroupStudyPlans
            migrationBuilder.AddColumn<Guid>(
                name: "PlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResolveToHead",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Ensure filtered unique index for current head exists (idempotent guard)
            migrationBuilder.Sql(@"
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes i WHERE i.name = 'UX_SP_Stable_Current'
)
BEGIN
    CREATE UNIQUE INDEX [UX_SP_Stable_Current]
    ON [StudyPlans].[StudyPlans]([StableId])
    WHERE [IsCurrent] = 1;
END

IF NOT EXISTS (
    SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID(N'[StudyPlans].[vLatestStudyPlans]')
)
BEGIN
    EXEC(N'CREATE VIEW [StudyPlans].[vLatestStudyPlans] AS SELECT * FROM [StudyPlans].[StudyPlans] WHERE [IsCurrent] = 1 OR [Status] = ''Current''');
END
ELSE
BEGIN
    EXEC(N'ALTER VIEW [StudyPlans].[vLatestStudyPlans] AS SELECT * FROM [StudyPlans].[StudyPlans] WHERE [IsCurrent] = 1 OR [Status] = ''Current''');
END

-- Backfill PlanVersionId where possible from pinned version
UPDATE gp
SET PlanVersionId = sp.Id
FROM [StudyGroups].[StudyGroupStudyPlans] gp
JOIN [StudyPlans].[StudyPlans] sp
  ON sp.StableId = gp.StudyPlanStableId AND sp.VersionNumber = ISNULL(gp.PinnedVersionNumber, sp.VersionNumber) AND sp.IsCurrent = 1;







            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.views WHERE object_id = OBJECT_ID(N'[StudyPlans].[vLatestStudyPlans]'))
    DROP VIEW [StudyPlans].[vLatestStudyPlans];
            ");

            migrationBuilder.DropColumn(
                name: "LockfileJson",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "PlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "ResolveToHead",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");
        }
    }
}

