using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    public partial class AddGroupPlanEnrollments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StudyPlans].[StudyPlanEnrollments]', N'U') IS NOT NULL
    DROP TABLE [StudyPlans].[StudyPlanEnrollments];

IF OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyPlans].[GroupPlanEnrollments] (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_GroupPlanEnrollments] PRIMARY KEY DEFAULT NEWID(),
        [GroupId] uniqueidentifier NOT NULL,
        [StudyPlanStableId] uniqueidentifier NOT NULL,
        [PlanVersionId] uniqueidentifier NOT NULL,
        [VersionPolicy] nvarchar(16) NOT NULL CONSTRAINT [DF_GroupPlanEnrollments_VersionPolicy] DEFAULT N'Current',
        [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_GroupPlanEnrollments_Status] DEFAULT N'Active',
        [CreatedBy] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_GroupPlanEnrollments_CreatedAt] DEFAULT SYSUTCDATETIME(),
        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_GroupPlanEnrollments_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [CK_GroupPlanEnrollments_VersionPolicy] CHECK ([VersionPolicy] IN (N'Current', N'Pinned')),
        CONSTRAINT [CK_GroupPlanEnrollments_Status] CHECK ([Status] IN (N'Active', N'Archived')),
        CONSTRAINT [FK_GroupPlanEnrollments_Groups_GroupId] FOREIGN KEY ([GroupId]) REFERENCES [Groups].[Groups]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_GroupPlanEnrollments_StudyPlans_PlanVersionId] FOREIGN KEY ([PlanVersionId]) REFERENCES [StudyPlans].[StudyPlans]([Id])
    );
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
      AND name = N'IX_GroupPlanEnrollments_GroupId_StudyPlanStableId'
)
BEGIN
    CREATE UNIQUE INDEX [IX_GroupPlanEnrollments_GroupId_StudyPlanStableId]
    ON [StudyPlans].[GroupPlanEnrollments]([GroupId], [StudyPlanStableId])
    WHERE [Status] = N'Active';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
      AND name = N'IX_GroupPlanEnrollments_StudyPlanStableId'
)
BEGIN
    CREATE INDEX [IX_GroupPlanEnrollments_StudyPlanStableId]
    ON [StudyPlans].[GroupPlanEnrollments]([StudyPlanStableId]);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]')
      AND name = N'IX_GroupPlanEnrollments_PlanVersionId'
)
BEGIN
    CREATE INDEX [IX_GroupPlanEnrollments_PlanVersionId]
    ON [StudyPlans].[GroupPlanEnrollments]([PlanVersionId]);
END;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]', N'U') IS NOT NULL
    DROP TABLE [StudyPlans].[GroupPlanEnrollments];");
        }
    }
}
