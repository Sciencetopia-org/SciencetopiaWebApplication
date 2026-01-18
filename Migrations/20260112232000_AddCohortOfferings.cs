using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sciencetopia.Data;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260112232000_AddCohortOfferings")]
    public partial class AddCohortOfferings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'StudyGroups')
    EXEC(N'CREATE SCHEMA [StudyGroups]');

-- Ensure StudyGroups table lives in StudyGroups schema (legacy could be dbo)
IF OBJECT_ID(N'[StudyGroups].[StudyGroups]', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[StudyGroups]', N'U') IS NOT NULL
        ALTER SCHEMA [StudyGroups] TRANSFER [dbo].[StudyGroups];
END

-- Keep StudyGroups PK column as Id (rename GroupId -> Id if needed)
IF OBJECT_ID(N'[StudyGroups].[StudyGroups]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('StudyGroups.StudyGroups','Id') IS NULL AND COL_LENGTH('StudyGroups.StudyGroups','GroupId') IS NOT NULL
        EXEC sp_rename N'[StudyGroups].[StudyGroups].[GroupId]', N'Id', 'COLUMN';
END

-- Ensure Cohorts table lives in StudyGroups schema (legacy could be dbo)
IF OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'[StudyGroups].[CohortGroups]', N'U') IS NOT NULL
        EXEC sp_rename N'[StudyGroups].[CohortGroups]', N'Cohorts';
    ELSE IF OBJECT_ID(N'[dbo].[Cohorts]', N'U') IS NOT NULL
        ALTER SCHEMA [StudyGroups] TRANSFER [dbo].[Cohorts];
    ELSE IF OBJECT_ID(N'[dbo].[CohortGroups]', N'U') IS NOT NULL
        ALTER SCHEMA [StudyGroups] TRANSFER [dbo].[CohortGroups];
END

DECLARE @cohortKeyCol sysname = NULL;
IF COL_LENGTH('StudyGroups.Cohorts','GroupId') IS NOT NULL SET @cohortKeyCol = N'GroupId';
ELSE IF COL_LENGTH('StudyGroups.Cohorts','Id') IS NOT NULL SET @cohortKeyCol = N'Id';
IF @cohortKeyCol IS NULL
    THROW 50000, 'Invariant failed: Cohorts missing GroupId/Id column.', 1;

-- Legacy column rename (ParentGroupId -> StudyGroupId)
IF COL_LENGTH('StudyGroups.Cohorts','ParentGroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','StudyGroupId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[ParentGroupId]', N'StudyGroupId', 'COLUMN';

-- Add CurrentOfferingId column when missing
IF COL_LENGTH('StudyGroups.Cohorts','CurrentOfferingId') IS NULL
    ALTER TABLE [StudyGroups].[Cohorts] ADD [CurrentOfferingId] uniqueidentifier NULL;

-- Create CohortOfferings table
IF OBJECT_ID(N'[StudyGroups].[CohortOfferings]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyGroups].[CohortOfferings] (
        [Id] uniqueidentifier NOT NULL,
        [CohortGroupId] uniqueidentifier NOT NULL,
        [StudyGroupStudyPlanId] uniqueidentifier NOT NULL,
        [StudyPlanVersionId] uniqueidentifier NOT NULL,
        [Status] nvarchar(20) NOT NULL CONSTRAINT [DF_CohortOfferings_Status] DEFAULT (N'Active'),
        [StartAt] datetime2 NOT NULL CONSTRAINT [DF_CohortOfferings_StartAt] DEFAULT (SYSUTCDATETIME()),
        [EndAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_CohortOfferings_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [CreatedBy] nvarchar(450) NULL
    );
END
IF OBJECT_ID(N'[StudyGroups].[CohortOfferings]', N'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE type = N'PK' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
        ALTER TABLE [StudyGroups].[CohortOfferings] ADD CONSTRAINT [PK_CohortOfferings] PRIMARY KEY ([Id]);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortOfferings_CohortGroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    CREATE INDEX [IX_CohortOfferings_CohortGroupId] ON [StudyGroups].[CohortOfferings] ([CohortGroupId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortOfferings_StudyGroupStudyPlanId' AND object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    CREATE INDEX [IX_CohortOfferings_StudyGroupStudyPlanId] ON [StudyGroups].[CohortOfferings] ([StudyGroupStudyPlanId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortOfferings_CohortGroupId_Status' AND object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    CREATE INDEX [IX_CohortOfferings_CohortGroupId_Status] ON [StudyGroups].[CohortOfferings] ([CohortGroupId], [Status]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortOfferings_StudyPlanVersionId' AND object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    CREATE INDEX [IX_CohortOfferings_StudyPlanVersionId] ON [StudyGroups].[CohortOfferings] ([StudyPlanVersionId]);

-- CohortOfferings FKs
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortOfferings_Cohorts_CohortGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
BEGIN
    DECLARE @fkSql nvarchar(max) = N'ALTER TABLE [StudyGroups].[CohortOfferings] WITH CHECK ADD CONSTRAINT [FK_CohortOfferings_Cohorts_CohortGroupId] ' +
        N'FOREIGN KEY ([CohortGroupId]) REFERENCES [StudyGroups].[Cohorts] (' + QUOTENAME(@cohortKeyCol) + N') ON DELETE CASCADE;';
    EXEC sp_executesql @fkSql;
END
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortOfferings_StudyGroupStudyPlans_StudyGroupStudyPlanId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    ALTER TABLE [StudyGroups].[CohortOfferings] WITH CHECK ADD CONSTRAINT [FK_CohortOfferings_StudyGroupStudyPlans_StudyGroupStudyPlanId]
        FOREIGN KEY ([StudyGroupStudyPlanId]) REFERENCES [StudyGroups].[StudyGroupStudyPlans] ([Id]) ON DELETE NO ACTION;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortOfferings_StudyPlans_StudyPlanVersionId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    ALTER TABLE [StudyGroups].[CohortOfferings] WITH CHECK ADD CONSTRAINT [FK_CohortOfferings_StudyPlans_StudyPlanVersionId]
        FOREIGN KEY ([StudyPlanVersionId]) REFERENCES [StudyPlans].[StudyPlans] ([Id]) ON DELETE NO ACTION;

-- Enforce unique adoption (StudyGroupId, StudyPlanStableId)
IF OBJECT_ID(N'[StudyGroups].[StudyGroupStudyPlans]', N'U') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1
        FROM [StudyGroups].[StudyGroupStudyPlans]
        GROUP BY [StudyGroupId], [StudyPlanStableId]
        HAVING COUNT(*) > 1
    )
        THROW 50000, 'Invariant failed: duplicate StudyGroupStudyPlans (StudyGroupId, StudyPlanStableId).', 1;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanStableId'
          AND object_id = OBJECT_ID(N'[StudyGroups].[StudyGroupStudyPlans]')
    )
        CREATE UNIQUE INDEX [IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanStableId]
            ON [StudyGroups].[StudyGroupStudyPlans] ([StudyGroupId], [StudyPlanStableId]);
END

-- Backfill CohortOfferings from Cohorts
DECLARE @planVersionCol sysname = NULL;
IF COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL SET @planVersionCol = N'PlanVersionId';
ELSE IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanVersionId') IS NOT NULL SET @planVersionCol = N'StudyPlanVersionId';
ELSE IF COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanVersionId') IS NOT NULL SET @planVersionCol = N'_legacy_StudyPlanVersionId';

IF @planVersionCol IS NULL
    THROW 50000, 'Invariant failed: no plan version column found on StudyGroups.Cohorts.', 1;

DECLARE @startAtExpr nvarchar(200) = CASE WHEN COL_LENGTH('StudyGroups.Cohorts','StartAt') IS NOT NULL THEN N'c.[StartAt]' ELSE N'NULL' END;
DECLARE @endAtExpr nvarchar(200) = CASE WHEN COL_LENGTH('StudyGroups.Cohorts','EndAt') IS NOT NULL THEN N'c.[EndAt]' ELSE N'NULL' END;

IF OBJECT_ID('tempdb..#CohortOfferingSeed') IS NOT NULL DROP TABLE #CohortOfferingSeed;
CREATE TABLE #CohortOfferingSeed (
    CohortGroupId uniqueidentifier NOT NULL,
    StudyGroupId uniqueidentifier NULL,
    PlanVersionId uniqueidentifier NULL,
    StableId uniqueidentifier NULL,
    StudyGroupStudyPlanId uniqueidentifier NULL,
    StartAt datetime2 NULL,
    EndAt datetime2 NULL,
    CreatedAt datetime2 NULL,
    CreatedBy nvarchar(450) NULL
);

DECLARE @sql nvarchar(max) = N'
INSERT INTO #CohortOfferingSeed (CohortGroupId, StudyGroupId, PlanVersionId, StableId, StudyGroupStudyPlanId, StartAt, EndAt, CreatedAt, CreatedBy)
SELECT c.' + QUOTENAME(@cohortKeyCol) + N', c.[StudyGroupId], c.' + QUOTENAME(@planVersionCol) + N' AS PlanVersionId,
       CASE WHEN p.[StableId] IS NULL OR p.[StableId] = ''00000000-0000-0000-0000-000000000000'' THEN p.[Id] ELSE p.[StableId] END AS StableId,
       sgsp.[Id] AS StudyGroupStudyPlanId,
       ' + @startAtExpr + N' AS StartAt,
       ' + @endAtExpr + N' AS EndAt,
       c.[CreatedAt], c.[CreatedBy]
FROM [StudyGroups].[Cohorts] c
LEFT JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = c.' + QUOTENAME(@planVersionCol) + N'
LEFT JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp
    ON sgsp.[StudyGroupId] = c.[StudyGroupId]
   AND sgsp.[StudyPlanStableId] = CASE WHEN p.[StableId] IS NULL OR p.[StableId] = ''00000000-0000-0000-0000-000000000000'' THEN p.[Id] ELSE p.[StableId] END;
';
EXEC sp_executesql @sql;

IF EXISTS (SELECT 1 FROM #CohortOfferingSeed WHERE PlanVersionId IS NULL)
    THROW 50000, 'Invariant failed: Cohorts missing PlanVersionId for offering backfill.', 1;
IF EXISTS (SELECT 1 FROM #CohortOfferingSeed WHERE StudyGroupId IS NULL)
    THROW 50000, 'Invariant failed: Cohorts missing StudyGroupId for offering backfill.', 1;
IF EXISTS (SELECT 1 FROM #CohortOfferingSeed WHERE StudyGroupStudyPlanId IS NULL)
    THROW 50000, 'Invariant failed: Cohorts missing StudyGroupStudyPlanId for offering backfill.', 1;

IF OBJECT_ID('tempdb..#NewOfferings') IS NOT NULL DROP TABLE #NewOfferings;
CREATE TABLE #NewOfferings (CohortGroupId uniqueidentifier NOT NULL, OfferingId uniqueidentifier NOT NULL);

INSERT INTO [StudyGroups].[CohortOfferings] ([Id],[CohortGroupId],[StudyGroupStudyPlanId],[StudyPlanVersionId],[Status],[StartAt],[EndAt],[CreatedAt],[CreatedBy])
OUTPUT inserted.[CohortGroupId], inserted.[Id] INTO #NewOfferings
SELECT NEWID(), s.[CohortGroupId], s.[StudyGroupStudyPlanId], s.[PlanVersionId], N'Active',
       COALESCE(s.[StartAt], s.[CreatedAt], SYSUTCDATETIME()), s.[EndAt],
       COALESCE(s.[CreatedAt], SYSUTCDATETIME()), s.[CreatedBy]
FROM #CohortOfferingSeed s
WHERE NOT EXISTS (
    SELECT 1 FROM [StudyGroups].[CohortOfferings] o
    WHERE o.[CohortGroupId] = s.[CohortGroupId] AND o.[Status] = N'Active'
);

DECLARE @updateSql nvarchar(max) = N'
UPDATE c
SET c.[CurrentOfferingId] = ISNULL(c.[CurrentOfferingId], n.[OfferingId])
FROM [StudyGroups].[Cohorts] c
JOIN #NewOfferings n ON n.[CohortGroupId] = c.' + QUOTENAME(@cohortKeyCol) + N'
WHERE c.[CurrentOfferingId] IS NULL;

UPDATE c
SET c.[CurrentOfferingId] = o.[Id]
FROM [StudyGroups].[Cohorts] c
JOIN [StudyGroups].[CohortOfferings] o ON o.[CohortGroupId] = c.' + QUOTENAME(@cohortKeyCol) + N' AND o.[Status] = N''Active''
WHERE c.[CurrentOfferingId] IS NULL;
';
EXEC sp_executesql @updateSql;

-- Enforce CurrentOfferingId not null if backfill complete
IF NOT EXISTS (SELECT 1 FROM [StudyGroups].[Cohorts] WHERE [CurrentOfferingId] IS NULL)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_CohortOfferings_CurrentOfferingId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_CohortOfferings_CurrentOfferingId];
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_CurrentOfferingId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        DROP INDEX [IX_Cohorts_CurrentOfferingId] ON [StudyGroups].[Cohorts];
    ALTER TABLE [StudyGroups].[Cohorts] ALTER COLUMN [CurrentOfferingId] uniqueidentifier NOT NULL;
END

-- Add FK + index for CurrentOfferingId
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_CurrentOfferingId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    CREATE UNIQUE INDEX [IX_Cohorts_CurrentOfferingId] ON [StudyGroups].[Cohorts] ([CurrentOfferingId]) WHERE [CurrentOfferingId] IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_CohortOfferings_CurrentOfferingId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_CohortOfferings_CurrentOfferingId]
        FOREIGN KEY ([CurrentOfferingId]) REFERENCES [StudyGroups].[CohortOfferings] ([Id]) ON DELETE NO ACTION;

-- Drop legacy FK/index on PlanVersionId before renaming
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyPlans_PlanVersionId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_StudyPlans_PlanVersionId];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_PlanVersionId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    DROP INDEX [IX_Cohorts_PlanVersionId] ON [StudyGroups].[Cohorts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_StudyPlanStableId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    DROP INDEX [IX_Cohorts_StudyPlanStableId] ON [StudyGroups].[Cohorts];

-- Rename legacy columns
IF COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanVersionId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[PlanVersionId]', N'_legacy_StudyPlanVersionId', 'COLUMN';
IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanVersionId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[StudyPlanVersionId]', N'_legacy_StudyPlanVersionId', 'COLUMN';
IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanStableId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanStableId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[StudyPlanStableId]', N'_legacy_StudyPlanStableId', 'COLUMN';
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Drop FK for CurrentOfferingId
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_CohortOfferings_CurrentOfferingId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_CohortOfferings_CurrentOfferingId];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_CurrentOfferingId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    DROP INDEX [IX_Cohorts_CurrentOfferingId] ON [StudyGroups].[Cohorts];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortOfferings_StudyPlanVersionId' AND object_id = OBJECT_ID(N'[StudyGroups].[CohortOfferings]'))
    DROP INDEX [IX_CohortOfferings_StudyPlanVersionId] ON [StudyGroups].[CohortOfferings];

IF COL_LENGTH('StudyGroups.Cohorts','CurrentOfferingId') IS NOT NULL
    ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [CurrentOfferingId];

-- Rename legacy columns back (best-effort)
IF COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[_legacy_StudyPlanVersionId]', N'PlanVersionId', 'COLUMN';
IF COL_LENGTH('StudyGroups.Cohorts','_legacy_StudyPlanStableId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','StudyPlanStableId') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[_legacy_StudyPlanStableId]', N'StudyPlanStableId', 'COLUMN';

IF OBJECT_ID(N'[StudyGroups].[CohortOfferings]', N'U') IS NOT NULL
    DROP TABLE [StudyGroups].[CohortOfferings];
");
        }
    }
}
