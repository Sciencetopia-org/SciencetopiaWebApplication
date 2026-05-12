using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AlignCohortSchemaAfterSharingRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Groups.Groups: align column/index names with the current model.
IF COL_LENGTH('Groups.Groups', 'GroupId') IS NOT NULL AND COL_LENGTH('Groups.Groups', 'Id') IS NULL
    EXEC sp_rename N'[Groups].[Groups].[GroupId]', N'Id', 'COLUMN';

IF COL_LENGTH('Groups.Groups', 'Type') IS NOT NULL AND COL_LENGTH('Groups.Groups', 'Kind') IS NULL
    EXEC sp_rename N'[Groups].[Groups].[Type]', N'Kind', 'COLUMN';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Groups].[Groups]') AND name = N'IX_Groups_Type')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Groups].[Groups]') AND name = N'IX_Groups_Kind')
    EXEC sp_rename N'[Groups].[Groups].[IX_Groups_Type]', N'IX_Groups_Kind', N'INDEX';

-- StudyGroups.Cohorts: align column names with the current model.
IF COL_LENGTH('StudyGroups.Cohorts', 'GroupId') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts', 'Id') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[GroupId]', N'Id', 'COLUMN';

IF COL_LENGTH('StudyGroups.Cohorts', 'EnrollmentPolicy') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts', 'EnrollMode') IS NULL
    EXEC sp_rename N'[StudyGroups].[Cohorts].[EnrollmentPolicy]', N'EnrollMode', 'COLUMN';

IF COL_LENGTH('StudyGroups.Cohorts', 'EnrollmentPolicy') IS NOT NULL AND COL_LENGTH('StudyGroups.Cohorts', 'EnrollMode') IS NOT NULL
BEGIN
    EXEC(N'UPDATE [StudyGroups].[Cohorts] SET [EnrollMode] = COALESCE([EnrollMode], [EnrollmentPolicy])');
    EXEC(N'ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [EnrollmentPolicy]');
END

IF COL_LENGTH('StudyGroups.Cohorts', 'Status') IS NOT NULL
    ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN [Status];

-- Retired plan columns are replaced by CohortOfferings.
DECLARE @retiredColumns TABLE ([Name] sysname NOT NULL PRIMARY KEY);
INSERT INTO @retiredColumns ([Name])
SELECT [Name]
FROM (VALUES
    (N'_legacy_StudyPlanStableId'),
    (N'_legacy_StudyPlanVersionId'),
    (N'StudyPlanStableId'),
    (N'PlanVersionId'),
    (N'StudyPlanVersionId')
) AS v([Name])
WHERE COL_LENGTH('StudyGroups.Cohorts', v.[Name]) IS NOT NULL;

DECLARE @retiredColumn sysname;
DECLARE retired_columns_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT [Name] FROM @retiredColumns;

OPEN retired_columns_cursor;
FETCH NEXT FROM retired_columns_cursor INTO @retiredColumn;

WHILE @@FETCH_STATUS = 0
BEGIN
    DECLARE @dropDependenciesSql nvarchar(max) = N'';

    SELECT @dropDependenciesSql = @dropDependenciesSql +
        N'ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
    FROM sys.foreign_keys fk
    WHERE fk.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
      AND EXISTS (
          SELECT 1
          FROM sys.foreign_key_columns fkc
          INNER JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
          WHERE fkc.constraint_object_id = fk.object_id
            AND c.name = @retiredColumn
      );

    SELECT @dropDependenciesSql = @dropDependenciesSql +
        N'ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
      AND c.name = @retiredColumn;

    SELECT @dropDependenciesSql = @dropDependenciesSql +
        N'ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';'
    FROM sys.check_constraints cc
    WHERE cc.parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
      AND cc.definition LIKE N'%' + @retiredColumn + N'%';

    SELECT @dropDependenciesSql = @dropDependenciesSql +
        N'DROP INDEX ' + QUOTENAME(i.name) + N' ON [StudyGroups].[Cohorts];'
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]')
      AND i.is_primary_key = 0
      AND i.is_unique_constraint = 0
      AND EXISTS (
          SELECT 1
          FROM sys.index_columns ic
          INNER JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
          WHERE ic.object_id = i.object_id
            AND ic.index_id = i.index_id
            AND c.name = @retiredColumn
      );

    IF @dropDependenciesSql <> N''
        EXEC sp_executesql @dropDependenciesSql;

    DECLARE @dropColumnSql nvarchar(max) =
        N'ALTER TABLE [StudyGroups].[Cohorts] DROP COLUMN ' + QUOTENAME(@retiredColumn) + N';';
    EXEC sp_executesql @dropColumnSql;

    FETCH NEXT FROM retired_columns_cursor INTO @retiredColumn;
END

CLOSE retired_columns_cursor;
DEALLOCATE retired_columns_cursor;

-- CurrentOfferingId must be nullable so Cohorts and CohortOfferings can be inserted in sequence.
IF COL_LENGTH('StudyGroups.Cohorts', 'CurrentOfferingId') IS NULL
    ALTER TABLE [StudyGroups].[Cohorts] ADD [CurrentOfferingId] uniqueidentifier NULL;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_CohortOfferings_CurrentOfferingId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_CohortOfferings_CurrentOfferingId];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'IX_Cohorts_CurrentOfferingId')
    DROP INDEX [IX_Cohorts_CurrentOfferingId] ON [StudyGroups].[Cohorts];

IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'StudyGroups'
      AND TABLE_NAME = 'Cohorts'
      AND COLUMN_NAME = 'CurrentOfferingId'
      AND IS_NULLABLE = 'NO'
)
    ALTER TABLE [StudyGroups].[Cohorts] ALTER COLUMN [CurrentOfferingId] uniqueidentifier NULL;

CREATE UNIQUE INDEX [IX_Cohorts_CurrentOfferingId]
    ON [StudyGroups].[Cohorts]([CurrentOfferingId])
    WHERE [CurrentOfferingId] IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_CohortOfferings_CurrentOfferingId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_CohortOfferings_CurrentOfferingId]
        FOREIGN KEY([CurrentOfferingId]) REFERENCES [StudyGroups].[CohortOfferings]([Id]);

-- Constraint/index names: align old CohortGroups names with current Cohorts names.
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'PK_CohortGroups')
   AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'PK_Cohorts')
    EXEC sp_rename N'[StudyGroups].[PK_CohortGroups]', N'PK_Cohorts', N'OBJECT';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'IX_CohortGroups_ParentGroupId')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'IX_Cohorts_StudyGroupId')
    EXEC sp_rename N'[StudyGroups].[Cohorts].[IX_CohortGroups_ParentGroupId]', N'IX_Cohorts_StudyGroupId', N'INDEX';

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortGroups_Groups_Id' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_CohortGroups_Groups_Id];

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_Groups_GroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_Groups_GroupId];

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_Groups_Id' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_Groups_Id]
        FOREIGN KEY([Id]) REFERENCES [Groups].[Groups]([Id]) ON DELETE CASCADE;

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortGroups_Groups_ParentGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_CohortGroups_Groups_ParentGroupId];

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyGroups_StudyGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_StudyGroups_StudyGroupId];

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyGroups_StudyGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_Cohorts_StudyGroups_StudyGroupId]
        FOREIGN KEY([StudyGroupId]) REFERENCES [StudyGroups].[StudyGroups]([Id]) ON DELETE NO ACTION;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- This migration formalizes the current Cohort schema. Recreating retired plan columns
-- would lose CohortOfferings semantics, so Down only reverts non-destructive names.
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Cohorts_StudyGroups_StudyGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] DROP CONSTRAINT [FK_Cohorts_StudyGroups_StudyGroupId];

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_CohortGroups_Groups_ParentGroupId' AND parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]'))
    ALTER TABLE [StudyGroups].[Cohorts] WITH CHECK ADD CONSTRAINT [FK_CohortGroups_Groups_ParentGroupId]
        FOREIGN KEY([StudyGroupId]) REFERENCES [Groups].[Groups]([Id]) ON DELETE NO ACTION;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'PK_Cohorts')
   AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE parent_object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'PK_CohortGroups')
    EXEC sp_rename N'[StudyGroups].[PK_Cohorts]', N'PK_CohortGroups', N'OBJECT';

IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'IX_Cohorts_StudyGroupId')
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[StudyGroups].[Cohorts]') AND name = N'IX_CohortGroups_ParentGroupId')
    EXEC sp_rename N'[StudyGroups].[Cohorts].[IX_Cohorts_StudyGroupId]', N'IX_CohortGroups_ParentGroupId', N'INDEX';
");
        }
    }
}
