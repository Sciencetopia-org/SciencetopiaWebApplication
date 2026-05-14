using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddStudyPlanLastStudiedAtAndModelAlignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'StudyPlans.StudyPlans', N'LastStudiedAt') IS NULL
BEGIN
    ALTER TABLE [StudyPlans].[StudyPlans] ADD [LastStudiedAt] datetime2 NULL;
END;

IF OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_GroupPlanEnrollments_StudyPlanEntity_PlanVersionId'
          AND parent_object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
    )
        ALTER TABLE [StudyPlans].[GroupPlanEnrollments]
        DROP CONSTRAINT [FK_GroupPlanEnrollments_StudyPlanEntity_PlanVersionId];

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE [name] = N'FK_GroupPlanEnrollments_StudyPlans_PlanVersionId'
          AND parent_object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
    )
        ALTER TABLE [StudyPlans].[GroupPlanEnrollments]
        ADD CONSTRAINT [FK_GroupPlanEnrollments_StudyPlans_PlanVersionId]
        FOREIGN KEY ([PlanVersionId]) REFERENCES [StudyPlans].[StudyPlans]([Id]) ON DELETE NO ACTION;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_GroupPlanEnrollments_Status'
          AND parent_object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
    )
        ALTER TABLE [StudyPlans].[GroupPlanEnrollments]
        ADD CONSTRAINT [CK_GroupPlanEnrollments_Status]
        CHECK ([Status] IN (N'Active', N'Archived'));

    IF NOT EXISTS (
        SELECT 1
        FROM sys.check_constraints
        WHERE [name] = N'CK_GroupPlanEnrollments_VersionPolicy'
          AND parent_object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
    )
        ALTER TABLE [StudyPlans].[GroupPlanEnrollments]
        ADD CONSTRAINT [CK_GroupPlanEnrollments_VersionPolicy]
        CHECK ([VersionPolicy] IN (N'Current', N'Pinned'));
END;

IF OBJECT_ID(N'[dbo].[StudyPlanEntity]', N'U') IS NOT NULL
    DROP TABLE [dbo].[StudyPlanEntity];

DECLARE @startAtDefault sysname;
SELECT @startAtDefault = dc.[name]
FROM sys.default_constraints dc
JOIN sys.columns c ON c.default_object_id = dc.object_id
WHERE dc.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
  AND c.[name] = N'StartAt';

IF @startAtDefault IS NOT NULL
BEGIN
    DECLARE @dropStartAtDefaultSql nvarchar(max) =
        N'ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT ' + QUOTENAME(@startAtDefault);
    EXEC sp_executesql @dropStartAtDefaultSql;
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'StudyPlans.StudyPlans', N'LastStudiedAt') IS NOT NULL
BEGIN
    ALTER TABLE [StudyPlans].[StudyPlans] DROP COLUMN [LastStudiedAt];
END;
");
        }
    }
}
