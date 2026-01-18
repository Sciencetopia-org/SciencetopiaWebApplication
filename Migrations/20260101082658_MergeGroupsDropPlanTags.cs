using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class MergeGroupsDropPlanTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StudyPlans].[StudyPlanTagAssignments]', N'U') IS NOT NULL
    DROP TABLE [StudyPlans].[StudyPlanTagAssignments];
");

            migrationBuilder.Sql(@"
SET DEADLOCK_PRIORITY HIGH;
SET LOCK_TIMEOUT 60000;

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'Groups')
    EXEC(N'CREATE SCHEMA [Groups]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'StudyGroups')
    EXEC(N'CREATE SCHEMA [StudyGroups]');
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'StudyPlans')
    EXEC(N'CREATE SCHEMA [StudyPlans]');

-- Ensure Groups.Groups exists or transfer StudyGroups.Groups
IF OBJECT_ID(N'[Groups].[Groups]', N'U') IS NULL AND OBJECT_ID(N'[StudyGroups].[Groups]', N'U') IS NOT NULL
    ALTER SCHEMA [Groups] TRANSFER [StudyGroups].[Groups];

IF OBJECT_ID(N'[Groups].[Groups]', N'U') IS NULL
BEGIN
    CREATE TABLE [Groups].[Groups] (
        [GroupId] uniqueidentifier NOT NULL,
        [Type] nvarchar(32) NOT NULL,
        [CreatedByUserId] nvarchar(450) NULL,
        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_Groups_CreatedAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_Groups_UpdatedAt] DEFAULT (SYSUTCDATETIME()),
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Groups] PRIMARY KEY ([GroupId])
    );
    CREATE INDEX [IX_Groups_Type] ON [Groups].[Groups] ([Type]);
END
ELSE
BEGIN
    IF COL_LENGTH('Groups.Groups','Id') IS NOT NULL AND COL_LENGTH('Groups.Groups','GroupId') IS NULL
        EXEC sp_rename N'[Groups].[Groups].[Id]', N'GroupId', 'COLUMN';
    IF COL_LENGTH('Groups.Groups','Kind') IS NOT NULL AND COL_LENGTH('Groups.Groups','Type') IS NULL
        EXEC sp_rename N'[Groups].[Groups].[Kind]', N'Type', 'COLUMN';

    IF COL_LENGTH('Groups.Groups','CreatedByUserId') IS NULL
        ALTER TABLE [Groups].[Groups] ADD [CreatedByUserId] nvarchar(450) NULL;
    IF COL_LENGTH('Groups.Groups','CreatedAt') IS NULL
        ALTER TABLE [Groups].[Groups] ADD [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_Groups_CreatedAt] DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('Groups.Groups','UpdatedAt') IS NULL
        ALTER TABLE [Groups].[Groups] ADD [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_Groups_UpdatedAt] DEFAULT (SYSUTCDATETIME());
    IF COL_LENGTH('Groups.Groups','RowVersion') IS NULL
        ALTER TABLE [Groups].[Groups] ADD [RowVersion] rowversion NOT NULL;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Groups_Type' AND object_id = OBJECT_ID(N'[Groups].[Groups]'))
        CREATE INDEX [IX_Groups_Type] ON [Groups].[Groups] ([Type]);
END

-- Merge StudyGroups.Groups data if still present
IF OBJECT_ID(N'[StudyGroups].[Groups]', N'U') IS NOT NULL
BEGIN
    DECLARE @sgIdCol sysname = NULL;
    DECLARE @sgTypeCol sysname = NULL;
    IF COL_LENGTH('StudyGroups.Groups','GroupId') IS NOT NULL SET @sgIdCol = 'GroupId';
    IF @sgIdCol IS NULL AND COL_LENGTH('StudyGroups.Groups','Id') IS NOT NULL SET @sgIdCol = 'Id';
    IF COL_LENGTH('StudyGroups.Groups','Type') IS NOT NULL SET @sgTypeCol = 'Type';
    IF @sgTypeCol IS NULL AND COL_LENGTH('StudyGroups.Groups','Kind') IS NOT NULL SET @sgTypeCol = 'Kind';

    IF @sgIdCol IS NOT NULL AND @sgTypeCol IS NOT NULL
    BEGIN
        DECLARE @sgInsertSql nvarchar(max) =
            N'INSERT INTO [Groups].[Groups] ([GroupId],[Type],[CreatedByUserId],[CreatedAt],[UpdatedAt]) ' +
            N'SELECT g.' + QUOTENAME(@sgIdCol) + N', g.' + QUOTENAME(@sgTypeCol) + N', g.[CreatedByUserId], g.[CreatedAt], g.[UpdatedAt] ' +
            N'FROM [StudyGroups].[Groups] g ' +
            N'WHERE NOT EXISTS (SELECT 1 FROM [Groups].[Groups] t WHERE t.[GroupId] = g.' + QUOTENAME(@sgIdCol) + N');';
        EXEC sp_executesql @sgInsertSql;
    END

    DECLARE @dropFkSql nvarchar(max) = N'';
    SELECT @dropFkSql += N'ALTER TABLE [' + s.name + N'].[' + t.name + N'] DROP CONSTRAINT [' + fk.name + N'];' + CHAR(13)
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON fk.parent_object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE fk.referenced_object_id = OBJECT_ID(N'[StudyGroups].[Groups]');
    IF @dropFkSql <> N'' EXEC sp_executesql @dropFkSql;

    DROP TABLE [StudyGroups].[Groups];
END

-- Normalize group type values
UPDATE [Groups].[Groups] SET [Type] = N'Cohort' WHERE [Type] = N'CohortGroup';

-- Rename StudyGroups.StudyGroups PK column to GroupId (drop legacy GroupId if duplicated)
IF OBJECT_ID(N'[StudyGroups].[StudyGroups]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('StudyGroups.StudyGroups','GroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.StudyGroups','Id') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[StudyGroups] SET [GroupId] = [Id] WHERE [GroupId] IS NULL;

        DECLARE @sgDropColFk nvarchar(max) = N'';
        SELECT @sgDropColFk += N'ALTER TABLE [' + s.name + N'].[' + t.name + N'] DROP CONSTRAINT [' + fk.name + N'];' + CHAR(13)
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON fk.parent_object_id = t.object_id
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        JOIN sys.columns c ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'[StudyGroups].[StudyGroups]')
          AND c.name = N'GroupId';
        IF @sgDropColFk <> N'' EXEC sp_executesql @sgDropColFk;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_StudyGroups_GroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[StudyGroups]'))
            DROP INDEX [IX_StudyGroups_GroupId] ON [StudyGroups].[StudyGroups];

        ALTER TABLE [StudyGroups].[StudyGroups] DROP COLUMN [GroupId];
    END

    IF COL_LENGTH('StudyGroups.StudyGroups','Id') IS NOT NULL AND COL_LENGTH('StudyGroups.StudyGroups','GroupId') IS NULL
        EXEC sp_rename N'[StudyGroups].[StudyGroups].[Id]', N'GroupId', 'COLUMN';
END

-- Rename CohortGroups to Cohorts and align columns
IF OBJECT_ID(N'[StudyGroups].[CohortGroups]', N'U') IS NOT NULL AND OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NULL
    EXEC sp_rename N'[StudyGroups].[CohortGroups]', N'Cohorts';

IF OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('StudyGroups.Cohorts','GroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','Id') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[Cohorts] SET [GroupId] = [Id] WHERE [GroupId] IS NULL;

        DECLARE @cohDropColFk nvarchar(max) = N'';
        SELECT @cohDropColFk += N'ALTER TABLE [' + s.name + N'].[' + t.name + N'] DROP CONSTRAINT [' + fk.name + N'];' + CHAR(13)
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON fk.parent_object_id = t.object_id
        JOIN sys.schemas s ON t.schema_id = s.schema_id
        JOIN sys.columns c ON fk.parent_object_id = c.object_id AND fk.parent_column_id = c.column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
          AND c.name = N'GroupId';
        IF @cohDropColFk <> N'' EXEC sp_executesql @cohDropColFk;

        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_GroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
            DROP INDEX [IX_Cohorts_GroupId] ON [StudyGroups].[Cohorts];
        IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortGroups_GroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
            DROP INDEX [IX_CohortGroups_GroupId] ON [StudyGroups].[Cohorts];

        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [GroupId];
    END

    IF COL_LENGTH('StudyGroups.Cohorts','Id') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','GroupId') IS NULL
        EXEC sp_rename N'[StudyGroups].[Cohorts].[Id]', N'GroupId', 'COLUMN';
    IF COL_LENGTH('StudyGroups.Cohorts','ParentGroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','StudyGroupId') IS NULL
        EXEC sp_rename N'[StudyGroups].[Cohorts].[ParentGroupId]', N'StudyGroupId', 'COLUMN';
    IF COL_LENGTH('StudyGroups.Cohorts','ParentGroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','StudyGroupId') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[Cohorts] SET [StudyGroupId] = [ParentGroupId] WHERE [StudyGroupId] IS NULL;
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [ParentGroupId];
    END
    IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NULL
        EXEC sp_rename N'[StudyGroups].[Cohorts].[StudyPlanVersionId]', N'PlanVersionId', 'COLUMN';
    IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[Cohorts] SET [PlanVersionId] = [StudyPlanVersionId] WHERE [PlanVersionId] IS NULL;
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [StudyPlanVersionId];
    END
    IF COL_LENGTH('StudyGroups.Cohorts','EnrollMode') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','EnrollmentPolicy') IS NULL
        EXEC sp_rename N'[StudyGroups].[Cohorts].[EnrollMode]', N'EnrollmentPolicy', 'COLUMN';
    IF COL_LENGTH('StudyGroups.Cohorts','EnrollMode') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','EnrollmentPolicy') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[Cohorts] SET [EnrollmentPolicy] = [EnrollMode] WHERE [EnrollmentPolicy] IS NULL;
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [EnrollMode];
    END

    IF COL_LENGTH('StudyGroups.Cohorts','EnrollmentPolicy') IS NULL
        ALTER TABLE [StudyGroups].[Cohorts] ADD [EnrollmentPolicy] nvarchar(16) NOT NULL CONSTRAINT [DF_Cohorts_EnrollmentPolicy] DEFAULT (N'OptIn');

    IF COL_LENGTH('StudyGroups.Cohorts','Status') IS NULL
        ALTER TABLE [StudyGroups].[Cohorts] ADD [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_Cohorts_Status] DEFAULT (N'Active');

    IF COL_LENGTH('StudyGroups.Cohorts','StudyGroupId') IS NOT NULL
        ALTER TABLE [StudyGroups].[Cohorts] ALTER COLUMN [StudyGroupId] uniqueidentifier NULL;

    -- Fill PlanVersionId from legacy columns if missing
    IF COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts','StudyPlanStableId') IS NOT NULL
    BEGIN
        UPDATE c
        SET PlanVersionId = pv.Id
        FROM [StudyGroups].[Cohorts] c
        OUTER APPLY (
            SELECT TOP 1 p.Id
            FROM [StudyPlans].[StudyPlans] p
            WHERE (p.StableId = c.StudyPlanStableId OR (p.StableId = CAST('00000000-0000-0000-0000-000000000000' AS uniqueidentifier) AND p.Id = c.StudyPlanStableId))
            ORDER BY CASE WHEN c.PinnedVersionNumber IS NOT NULL AND p.VersionNumber = c.PinnedVersionNumber THEN 0 ELSE 1 END,
                     p.IsCurrent DESC,
                     p.VersionNumber DESC
        ) pv
        WHERE c.PlanVersionId IS NULL
           OR NOT EXISTS (SELECT 1 FROM [StudyPlans].[StudyPlans] p WHERE p.Id = c.PlanVersionId);
    END

    IF COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL
       AND EXISTS (SELECT 1 FROM [StudyPlans].[StudyPlans])
    BEGIN
        UPDATE c
        SET PlanVersionId = pv.Id
        FROM [StudyGroups].[Cohorts] c
        CROSS APPLY (
            SELECT TOP 1 p.Id
            FROM [StudyPlans].[StudyPlans] p
            ORDER BY p.IsCurrent DESC, p.VersionNumber DESC
        ) pv
        WHERE c.PlanVersionId IS NULL;
    END

    -- Drop legacy indexes if present
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortGroups_StudyPlanStableId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        DROP INDEX [IX_CohortGroups_StudyPlanStableId] ON [StudyGroups].[Cohorts];
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortGroups_StudyPlanStableId_PinnedVersionNumber' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        DROP INDEX [IX_CohortGroups_StudyPlanStableId_PinnedVersionNumber] ON [StudyGroups].[Cohorts];
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CohortGroups_ParentGroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        DROP INDEX [IX_CohortGroups_ParentGroupId] ON [StudyGroups].[Cohorts];

    -- Drop legacy columns if present
    IF COL_LENGTH('StudyGroups.Cohorts','StudyPlanStableId') IS NOT NULL
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [StudyPlanStableId];
    IF COL_LENGTH('StudyGroups.Cohorts','PinnedVersionNumber') IS NOT NULL
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [PinnedVersionNumber];
    IF COL_LENGTH('StudyGroups.Cohorts','MembersCount') IS NOT NULL
        ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [MembersCount];

    -- Enforce PlanVersionId not null if possible
    IF COL_LENGTH('StudyGroups.Cohorts','PlanVersionId') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM [StudyGroups].[Cohorts] WHERE [PlanVersionId] IS NULL)
        ALTER TABLE [StudyGroups].[Cohorts] ALTER COLUMN [PlanVersionId] uniqueidentifier NOT NULL;

    IF COL_LENGTH('StudyGroups.Cohorts','EnrollmentPolicy') IS NOT NULL
    BEGIN
        UPDATE [StudyGroups].[Cohorts] SET [EnrollmentPolicy] = ISNULL([EnrollmentPolicy], N'OptIn');
        ALTER TABLE [StudyGroups].[Cohorts] ALTER COLUMN [EnrollmentPolicy] nvarchar(16) NOT NULL;
    END

    -- Indexes
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_StudyGroupId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        CREATE INDEX [IX_Cohorts_StudyGroupId] ON [StudyGroups].[Cohorts] ([StudyGroupId]);
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Cohorts_PlanVersionId' AND object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        CREATE INDEX [IX_Cohorts_PlanVersionId] ON [StudyGroups].[Cohorts] ([PlanVersionId]);
END

-- Ensure Groups rows for StudyGroups/Cohorts
IF OBJECT_ID(N'[StudyGroups].[StudyGroups]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [Groups].[Groups] ([GroupId],[Type],[CreatedByUserId],[CreatedAt],[UpdatedAt])
    SELECT sg.GroupId, N'StudyGroup', NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM [StudyGroups].[StudyGroups] sg
    WHERE NOT EXISTS (SELECT 1 FROM [Groups].[Groups] g WHERE g.GroupId = sg.GroupId);
END

IF OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [Groups].[Groups] ([GroupId],[Type],[CreatedByUserId],[CreatedAt],[UpdatedAt])
    SELECT c.GroupId, N'Cohort', NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM [StudyGroups].[Cohorts] c
    WHERE NOT EXISTS (SELECT 1 FROM [Groups].[Groups] g WHERE g.GroupId = c.GroupId);
END

UPDATE g
SET g.[Type] = N'StudyGroup'
FROM [Groups].[Groups] g
WHERE g.[Type] IS NULL AND EXISTS (SELECT 1 FROM [StudyGroups].[StudyGroups] sg WHERE sg.GroupId = g.GroupId);

UPDATE g
SET g.[Type] = N'Cohort'
FROM [Groups].[Groups] g
WHERE g.[Type] IS NULL AND EXISTS (SELECT 1 FROM [StudyGroups].[Cohorts] c WHERE c.GroupId = g.GroupId);

-- Rename legacy UserStudyPlanEnrollments if present
IF OBJECT_ID(N'[StudyPlans].[UserStudyPlanEnrollments]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[StudyPlans].[_legacy_UserStudyPlanEnrollments]', N'U') IS NULL
        EXEC sp_rename N'[StudyPlans].[UserStudyPlanEnrollments]', N'_legacy_UserStudyPlanEnrollments';
END

-- Create StudyPlanEnrollments if missing
IF OBJECT_ID(N'[StudyPlans].[StudyPlanEnrollments]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyPlans].[StudyPlanEnrollments] (
        [EnrollmentId] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [ScopeType] nvarchar(16) NOT NULL,
        [ScopeId] uniqueidentifier NOT NULL,
        [PlanVersionId] uniqueidentifier NOT NULL,
        [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_StudyPlanEnrollments_Status] DEFAULT (N'Active'),
        [EnrolledAt] datetimeoffset NOT NULL CONSTRAINT [DF_StudyPlanEnrollments_EnrolledAt] DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetimeoffset NULL,
        CONSTRAINT [PK_StudyPlanEnrollments] PRIMARY KEY ([EnrollmentId]),
        CONSTRAINT [CK_StudyPlanEnrollments_ScopeType] CHECK ([ScopeType] IN ('Cohort','Personal'))
    );
    CREATE INDEX [IX_StudyPlanEnrollments_UserId] ON [StudyPlans].[StudyPlanEnrollments] ([UserId]);
    CREATE INDEX [IX_StudyPlanEnrollments_Scope] ON [StudyPlans].[StudyPlanEnrollments] ([ScopeType],[ScopeId]);
    CREATE INDEX [IX_StudyPlanEnrollments_PlanVersionId] ON [StudyPlans].[StudyPlanEnrollments] ([PlanVersionId]);
    ALTER TABLE [StudyPlans].[StudyPlanEnrollments] WITH CHECK ADD CONSTRAINT [FK_StudyPlanEnrollments_StudyPlans_PlanVersionId]
        FOREIGN KEY ([PlanVersionId]) REFERENCES [StudyPlans].[StudyPlans]([Id]) ON DELETE NO ACTION;
    ALTER TABLE [StudyPlans].[StudyPlanEnrollments] WITH CHECK ADD CONSTRAINT [FK_StudyPlanEnrollments_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [Users].[AspNetUsers]([Id]) ON DELETE CASCADE;
END

-- Migrate enrollments from legacy table
IF OBJECT_ID(N'[StudyPlans].[_legacy_UserStudyPlanEnrollments]', N'U') IS NOT NULL
BEGIN
    ;WITH ranked AS (
        SELECT
            UserId,
            ScopeId = COALESCE(CohortGroupId, GroupId),
            ScopeType = CASE WHEN CohortGroupId IS NOT NULL THEN N'Cohort' ELSE N'Personal' END,
            PlanVersionId,
            Status,
            CreatedAt,
            UpdatedAt,
            ROW_NUMBER() OVER (
                PARTITION BY UserId, COALESCE(CohortGroupId, GroupId)
                ORDER BY CASE WHEN Status IN (N'Active', N'Learning') THEN 2 WHEN Status IN (N'Completed') THEN 1 ELSE 0 END DESC,
                         COALESCE(UpdatedAt, CreatedAt) DESC
            ) AS rn
        FROM [StudyPlans].[_legacy_UserStudyPlanEnrollments]
        WHERE PlanVersionId IS NOT NULL AND COALESCE(CohortGroupId, GroupId) IS NOT NULL
    )
    INSERT INTO [StudyPlans].[StudyPlanEnrollments] ([EnrollmentId],[UserId],[ScopeType],[ScopeId],[PlanVersionId],[Status],[EnrolledAt],[UpdatedAt])
    SELECT NEWID(), UserId, ScopeType, ScopeId, PlanVersionId, ISNULL(Status, N'Active'), COALESCE(CreatedAt, SYSUTCDATETIME()), UpdatedAt
    FROM ranked r
    WHERE r.rn = 1
      AND NOT EXISTS (
          SELECT 1 FROM [StudyPlans].[StudyPlanEnrollments] e
          WHERE e.UserId = r.UserId AND e.ScopeType = r.ScopeType AND e.ScopeId = r.ScopeId
      );
END

-- Ensure PersonalGroup rows exist for personal enrollments
IF OBJECT_ID(N'[StudyPlans].[StudyPlanEnrollments]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [Groups].[Groups] ([GroupId],[Type],[CreatedByUserId],[CreatedAt],[UpdatedAt])
    SELECT e.ScopeId, N'PersonalGroup', NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM [StudyPlans].[StudyPlanEnrollments] e
    WHERE e.ScopeType = N'Personal'
      AND NOT EXISTS (SELECT 1 FROM [Groups].[Groups] g WHERE g.GroupId = e.ScopeId);
END

-- Create Cohorts for legacy enrollments missing in Cohorts
IF OBJECT_ID(N'[StudyPlans].[_legacy_UserStudyPlanEnrollments]', N'U') IS NOT NULL AND OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NOT NULL
BEGIN
    ;WITH cohort_counts AS (
        SELECT CohortGroupId, PlanVersionId,
               COUNT(*) AS Cnt,
               MAX(UpdatedAt) AS MaxUpdated,
               MAX(CreatedAt) AS MaxCreated
        FROM [StudyPlans].[_legacy_UserStudyPlanEnrollments]
        WHERE CohortGroupId IS NOT NULL AND PlanVersionId IS NOT NULL
        GROUP BY CohortGroupId, PlanVersionId
    ),
    choice AS (
        SELECT CohortGroupId, PlanVersionId,
               ROW_NUMBER() OVER (PARTITION BY CohortGroupId ORDER BY Cnt DESC, MaxUpdated DESC, MaxCreated DESC) AS rn
        FROM cohort_counts
    ),
    cohort_src AS (
        SELECT c.CohortGroupId, c.PlanVersionId,
               MAX(CASE WHEN sg.GroupId IS NOT NULL THEN e.GroupId ELSE NULL END) AS StudyGroupId
        FROM choice c
        LEFT JOIN [StudyPlans].[_legacy_UserStudyPlanEnrollments] e ON e.CohortGroupId = c.CohortGroupId
        LEFT JOIN [StudyGroups].[StudyGroups] sg ON sg.GroupId = e.GroupId
        WHERE c.rn = 1
        GROUP BY c.CohortGroupId, c.PlanVersionId
    )
    INSERT INTO [StudyGroups].[Cohorts] ([GroupId],[StudyGroupId],[PlanVersionId],[EnrollmentPolicy],[Status],[CreatedAt])
    SELECT s.CohortGroupId, s.StudyGroupId, s.PlanVersionId, N'OptIn', N'Active', SYSUTCDATETIME()
    FROM cohort_src s
    WHERE NOT EXISTS (SELECT 1 FROM [StudyGroups].[Cohorts] c WHERE c.GroupId = s.CohortGroupId);

    INSERT INTO [Groups].[Groups] ([GroupId],[Type],[CreatedByUserId],[CreatedAt],[UpdatedAt])
    SELECT s.CohortGroupId, N'Cohort', NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
    FROM cohort_src s
    WHERE NOT EXISTS (SELECT 1 FROM [Groups].[Groups] g WHERE g.GroupId = s.CohortGroupId);
END

-- Recreate FKs for StudyGroups and Cohorts
IF OBJECT_ID(N'[StudyGroups].[StudyGroups]', N'U') IS NOT NULL
BEGIN
    DECLARE @fkSgDrop nvarchar(max) = N'';
    SELECT @fkSgDrop += N'ALTER TABLE [' + s.name + N'].[' + t.name + N'] DROP CONSTRAINT [' + fk.name + N'];' + CHAR(13)
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON fk.parent_object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE fk.parent_object_id = OBJECT_ID(N'[StudyGroups].[StudyGroups]')
      AND fk.referenced_object_id IN (OBJECT_ID(N'[Groups].[Groups]'), OBJECT_ID(N'[StudyGroups].[Groups]'));
    IF @fkSgDrop <> N'' EXEC sp_executesql @fkSgDrop;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_StudyGroups_Groups_GroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[StudyGroups]'))
        ALTER TABLE [StudyGroups].[StudyGroups] WITH CHECK ADD CONSTRAINT [FK_StudyGroups_Groups_GroupId]
            FOREIGN KEY ([GroupId]) REFERENCES [Groups].[Groups]([GroupId]) ON DELETE CASCADE;
END

IF OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NOT NULL
BEGIN
    DECLARE @fkCohDrop nvarchar(max) = N'';
    SELECT @fkCohDrop += N'ALTER TABLE [' + s.name + N'].[' + t.name + N'] DROP CONSTRAINT [' + fk.name + N'];' + CHAR(13)
    FROM sys.foreign_keys fk
    JOIN sys.tables t ON fk.parent_object_id = t.object_id
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE fk.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
      AND fk.referenced_object_id IN (OBJECT_ID(N'[Groups].[Groups]'), OBJECT_ID(N'[StudyGroups].[Groups]'), OBJECT_ID(N'[StudyPlans].[StudyPlans]'));
    IF @fkCohDrop <> N'' EXEC sp_executesql @fkCohDrop;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_Groups_GroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_Groups_GroupId]
            FOREIGN KEY ([GroupId]) REFERENCES [Groups].[Groups]([GroupId]) ON DELETE CASCADE;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyGroups_StudyGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_StudyGroups_StudyGroupId]
            FOREIGN KEY ([StudyGroupId]) REFERENCES [StudyGroups].[StudyGroups]([GroupId]) ON DELETE NO ACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyPlans_PlanVersionId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
        ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_StudyPlans_PlanVersionId]
            FOREIGN KEY ([PlanVersionId]) REFERENCES [StudyPlans].[StudyPlans]([Id]) ON DELETE NO ACTION;
END

-- Prepare legacy UserGroups table if present
IF OBJECT_ID(N'[Groups].[UserGroups]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('Groups.UserGroups','Id') IS NOT NULL OR COL_LENGTH('Groups.UserGroups','UserId') IS NULL OR COL_LENGTH('Groups.UserGroups','GroupId') IS NULL
    BEGIN
        IF OBJECT_ID(N'[Groups].[UserGroups_Legacy]', N'U') IS NULL
            EXEC sp_rename N'[Groups].[UserGroups]', N'UserGroups_Legacy';
    END
END

-- Create UserGroups if missing
IF OBJECT_ID(N'[Groups].[UserGroups]', N'U') IS NULL
BEGIN
    CREATE TABLE [Groups].[UserGroups] (
        [UserId] nvarchar(450) NOT NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [Role] tinyint NOT NULL,
        [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_UserGroups_Status] DEFAULT (N'Active'),
        [JoinedAt] datetimeoffset NOT NULL CONSTRAINT [DF_UserGroups_JoinedAt] DEFAULT (SYSUTCDATETIME()),
        [LeftAt] datetimeoffset NULL,
        CONSTRAINT [PK_UserGroups] PRIMARY KEY ([UserId],[GroupId])
    );
    CREATE INDEX [IX_UserGroups_GroupId] ON [Groups].[UserGroups] ([GroupId]);
    CREATE INDEX [IX_UserGroups_UserId] ON [Groups].[UserGroups] ([UserId]);
    CREATE INDEX [IX_UserGroups_GroupId_Status] ON [Groups].[UserGroups] ([GroupId],[Status]);
    ALTER TABLE [Groups].[UserGroups] WITH CHECK ADD CONSTRAINT [FK_UserGroups_Groups_GroupId]
        FOREIGN KEY ([GroupId]) REFERENCES [Groups].[Groups]([GroupId]) ON DELETE CASCADE;
    ALTER TABLE [Groups].[UserGroups] WITH CHECK ADD CONSTRAINT [FK_UserGroups_AspNetUsers_UserId]
        FOREIGN KEY ([UserId]) REFERENCES [Users].[AspNetUsers]([Id]) ON DELETE CASCADE;
END

-- Migrate from StudyGroups.GroupMembers
IF OBJECT_ID(N'[StudyGroups].[GroupMembers]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [Groups].[UserGroups] ([UserId],[GroupId],[Role],[Status],[JoinedAt],[LeftAt])
    SELECT gm.UserId, gm.GroupId, gm.Role, gm.Status, gm.JoinedAt, gm.LeftAt
    FROM [StudyGroups].[GroupMembers] gm
    WHERE NOT EXISTS (SELECT 1 FROM [Groups].[UserGroups] ug WHERE ug.UserId = gm.UserId AND ug.GroupId = gm.GroupId);

    IF OBJECT_ID(N'[StudyGroups].[GroupMembers_Legacy]', N'U') IS NULL
        EXEC sp_rename N'[StudyGroups].[GroupMembers]', N'GroupMembers_Legacy';
END

-- Migrate from legacy Groups.UserGroups if present
IF OBJECT_ID(N'[Groups].[UserGroups_Legacy]', N'U') IS NOT NULL
BEGIN
    DECLARE @ugGroupCol sysname = NULL;
    IF COL_LENGTH('Groups.UserGroups_Legacy','GroupId') IS NOT NULL SET @ugGroupCol = 'GroupId';
    IF @ugGroupCol IS NULL AND COL_LENGTH('Groups.UserGroups_Legacy','GroupID') IS NOT NULL SET @ugGroupCol = 'GroupID';

    IF @ugGroupCol IS NOT NULL
    BEGIN
        DECLARE @ugSql nvarchar(max) = N'INSERT INTO [Groups].[UserGroups] ([UserId],[GroupId],[Role],[Status],[JoinedAt])' +
            N' SELECT ug.UserId, TRY_CONVERT(uniqueidentifier, ug.[' + @ugGroupCol + N']), 0, ISNULL(ug.Status, N''Active''), ISNULL(ug.JoinedAt, SYSUTCDATETIME())' +
            N' FROM [Groups].[UserGroups_Legacy] ug' +
            N' WHERE ug.UserId IS NOT NULL AND ug.[' + @ugGroupCol + N'] IS NOT NULL' +
            N' AND NOT EXISTS (SELECT 1 FROM [Groups].[UserGroups] t WHERE t.UserId = ug.UserId AND t.GroupId = TRY_CONVERT(uniqueidentifier, ug.[' + @ugGroupCol + N']));';
        EXEC sp_executesql @ugSql;
    END
END

-- Migrate cohort membership from legacy enrollments
IF OBJECT_ID(N'[StudyPlans].[_legacy_UserStudyPlanEnrollments]', N'U') IS NOT NULL
BEGIN
    INSERT INTO [Groups].[UserGroups] ([UserId],[GroupId],[Role],[Status],[JoinedAt])
    SELECT e.UserId, e.CohortGroupId, 0, ISNULL(e.Status, N'Active'), ISNULL(e.CreatedAt, SYSUTCDATETIME())
    FROM [StudyPlans].[_legacy_UserStudyPlanEnrollments] e
    WHERE e.CohortGroupId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM [Groups].[UserGroups] ug WHERE ug.UserId = e.UserId AND ug.GroupId = e.CohortGroupId);
END
", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudyPlanTagAssignments",
                schema: "StudyPlans",
                columns: table => new
                {
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanVersionNumber = table.Column<int>(type: "int", nullable: false),
                    TagStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanTagAssignments", x => new { x.StudyPlanStableId, x.StudyPlanVersionNumber, x.TagStableId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTagAssignments_TagStableId",
                schema: "StudyPlans",
                table: "StudyPlanTagAssignments",
                column: "TagStableId");
        }
    }
}
