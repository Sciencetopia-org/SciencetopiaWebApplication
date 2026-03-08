SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF SCHEMA_ID(N'Groups') IS NULL
    BEGIN
        EXEC(N'CREATE SCHEMA [Groups]');
    END

    IF OBJECT_ID(N'[Groups].[UserGroups]', N'U') IS NULL
    BEGIN
        CREATE TABLE [Groups].[UserGroups] (
            [UserId] nvarchar(450) NOT NULL,
            [GroupId] uniqueidentifier NOT NULL,
            [Role] tinyint NOT NULL CONSTRAINT [DF_UserGroups_Role] DEFAULT ((0)),
            [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_UserGroups_Status] DEFAULT (N'Active'),
            [JoinedAt] datetimeoffset NOT NULL CONSTRAINT [DF_UserGroups_JoinedAt] DEFAULT (SYSUTCDATETIME()),
            [LeftAt] datetimeoffset NULL,
            CONSTRAINT [PK_UserGroups] PRIMARY KEY ([UserId], [GroupId])
        );
    END
    ELSE
    BEGIN
        -- Normalize legacy column names first.
        IF COL_LENGTH('Groups.UserGroups', 'UserId') IS NULL AND COL_LENGTH('Groups.UserGroups', 'UserID') IS NOT NULL
            EXEC sp_rename N'[Groups].[UserGroups].[UserID]', N'UserId', N'COLUMN';

        IF COL_LENGTH('Groups.UserGroups', 'GroupId') IS NULL AND COL_LENGTH('Groups.UserGroups', 'GroupID') IS NOT NULL
            EXEC sp_rename N'[Groups].[UserGroups].[GroupID]', N'GroupId', N'COLUMN';

        -- Role
        IF COL_LENGTH('Groups.UserGroups', 'Role') IS NULL
            ALTER TABLE [Groups].[UserGroups]
                ADD [Role] tinyint NOT NULL CONSTRAINT [DF_UserGroups_Role] DEFAULT ((0));

        -- Status
        IF COL_LENGTH('Groups.UserGroups', 'Status') IS NULL
            ALTER TABLE [Groups].[UserGroups]
                ADD [Status] nvarchar(16) NOT NULL CONSTRAINT [DF_UserGroups_Status] DEFAULT (N'Active');

        -- JoinedAt (migrate from legacy JoinDate/JoinedDate if present)
        IF COL_LENGTH('Groups.UserGroups', 'JoinedAt') IS NULL
        BEGIN
            ALTER TABLE [Groups].[UserGroups] ADD [JoinedAt] datetimeoffset NULL;

            IF COL_LENGTH('Groups.UserGroups', 'JoinDate') IS NOT NULL
            BEGIN
                EXEC(N'
UPDATE [Groups].[UserGroups]
SET [JoinedAt] = COALESCE(TRY_CONVERT(datetimeoffset, [JoinDate]), SYSUTCDATETIME());');
            END
            ELSE IF COL_LENGTH('Groups.UserGroups', 'JoinedDate') IS NOT NULL
            BEGIN
                EXEC(N'
UPDATE [Groups].[UserGroups]
SET [JoinedAt] = COALESCE(TRY_CONVERT(datetimeoffset, [JoinedDate]), SYSUTCDATETIME());');
            END
            ELSE
            BEGIN
                EXEC(N'
UPDATE [Groups].[UserGroups]
SET [JoinedAt] = SYSUTCDATETIME();');
            END
        END

        -- LeftAt (migrate from legacy LeftDate if present)
        IF COL_LENGTH('Groups.UserGroups', 'LeftAt') IS NULL
        BEGIN
            ALTER TABLE [Groups].[UserGroups] ADD [LeftAt] datetimeoffset NULL;

            IF COL_LENGTH('Groups.UserGroups', 'LeftDate') IS NOT NULL
            BEGIN
                EXEC(N'
UPDATE [Groups].[UserGroups]
SET [LeftAt] = TRY_CONVERT(datetimeoffset, [LeftDate]);');
            END
        END

        -- Data cleanup before NOT NULL enforcement.
        IF COL_LENGTH('Groups.UserGroups', 'Status') IS NOT NULL
        BEGIN
            EXEC(N'
UPDATE [Groups].[UserGroups]
SET [Status] = N''Active''
WHERE [Status] IS NULL OR LTRIM(RTRIM([Status])) = N'''';');
        END

        IF COL_LENGTH('Groups.UserGroups', 'JoinedAt') IS NOT NULL
        BEGIN
            EXEC(N'
UPDATE [Groups].[UserGroups]
SET [JoinedAt] = SYSUTCDATETIME()
WHERE [JoinedAt] IS NULL;');
        END

        -- Enforce target nullability.
        IF COL_LENGTH('Groups.UserGroups', 'Role') IS NOT NULL
            EXEC(N'ALTER TABLE [Groups].[UserGroups] ALTER COLUMN [Role] tinyint NOT NULL;');
        IF COL_LENGTH('Groups.UserGroups', 'Status') IS NOT NULL
            EXEC(N'ALTER TABLE [Groups].[UserGroups] ALTER COLUMN [Status] nvarchar(16) NOT NULL;');
        IF COL_LENGTH('Groups.UserGroups', 'JoinedAt') IS NOT NULL
            EXEC(N'ALTER TABLE [Groups].[UserGroups] ALTER COLUMN [JoinedAt] datetimeoffset NOT NULL;');

        -- Ensure default constraint for JoinedAt exists.
        IF NOT EXISTS (
            SELECT 1
            FROM sys.default_constraints dc
            JOIN sys.columns c
              ON dc.parent_object_id = c.object_id
             AND dc.parent_column_id = c.column_id
            WHERE dc.parent_object_id = OBJECT_ID(N'[Groups].[UserGroups]')
              AND c.name = N'JoinedAt'
        )
        BEGIN
            EXEC(N'ALTER TABLE [Groups].[UserGroups]
                ADD CONSTRAINT [DF_UserGroups_JoinedAt] DEFAULT (SYSUTCDATETIME()) FOR [JoinedAt];');
        END

        -- Optional: remove legacy date columns after migration.
        IF COL_LENGTH('Groups.UserGroups', 'JoinDate') IS NOT NULL
           OR COL_LENGTH('Groups.UserGroups', 'JoinedDate') IS NOT NULL
           OR COL_LENGTH('Groups.UserGroups', 'LeftDate') IS NOT NULL
        BEGIN
            DECLARE @legacyCol sysname;
            DECLARE legacy_cols_cursor CURSOR LOCAL FAST_FORWARD FOR
                SELECT [name]
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'[Groups].[UserGroups]')
                  AND [name] IN (N'JoinDate', N'JoinedDate', N'LeftDate');

            OPEN legacy_cols_cursor;
            FETCH NEXT FROM legacy_cols_cursor INTO @legacyCol;

            WHILE @@FETCH_STATUS = 0
            BEGIN
                DECLARE @dropDepsSql nvarchar(max) = N'';

                -- Drop default constraints bound to legacy column
                SELECT @dropDepsSql = @dropDepsSql +
                    N'ALTER TABLE [Groups].[UserGroups] DROP CONSTRAINT [' + dc.name + N'];' + CHAR(10)
                FROM sys.default_constraints dc
                INNER JOIN sys.columns c
                    ON c.object_id = dc.parent_object_id
                   AND c.column_id = dc.parent_column_id
                WHERE dc.parent_object_id = OBJECT_ID(N'[Groups].[UserGroups]')
                  AND c.name = @legacyCol;

                -- Drop non-PK/non-unique-constraint indexes that reference legacy column
                SELECT @dropDepsSql = @dropDepsSql +
                    N'DROP INDEX [' + i.name + N'] ON [Groups].[UserGroups];' + CHAR(10)
                FROM sys.indexes i
                INNER JOIN sys.index_columns ic
                    ON ic.object_id = i.object_id
                   AND ic.index_id = i.index_id
                INNER JOIN sys.columns c
                    ON c.object_id = ic.object_id
                   AND c.column_id = ic.column_id
                WHERE i.object_id = OBJECT_ID(N'[Groups].[UserGroups]')
                  AND c.name = @legacyCol
                  AND i.is_primary_key = 0
                  AND i.is_unique_constraint = 0
                  AND i.name IS NOT NULL;

                IF @dropDepsSql <> N''
                    EXEC sp_executesql @dropDepsSql;

                DECLARE @dropColSql nvarchar(max) =
                    N'ALTER TABLE [Groups].[UserGroups] DROP COLUMN ' + QUOTENAME(@legacyCol) + N';';
                EXEC sp_executesql @dropColSql;
                FETCH NEXT FROM legacy_cols_cursor INTO @legacyCol;
            END

            CLOSE legacy_cols_cursor;
            DEALLOCATE legacy_cols_cursor;
        END
    END

    -- Add PK if missing (composite UserId+GroupId).
    IF NOT EXISTS (
        SELECT 1
        FROM sys.key_constraints kc
        WHERE kc.parent_object_id = OBJECT_ID(N'[Groups].[UserGroups]')
          AND kc.type = 'PK'
    )
    BEGIN
        ALTER TABLE [Groups].[UserGroups]
            ADD CONSTRAINT [PK_UserGroups] PRIMARY KEY ([UserId], [GroupId]);
    END

    -- Standard indexes.
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Groups].[UserGroups]')
          AND name = N'IX_UserGroups_GroupId'
    )
        CREATE INDEX [IX_UserGroups_GroupId] ON [Groups].[UserGroups]([GroupId]);

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'[Groups].[UserGroups]')
          AND name = N'IX_UserGroups_UserId'
    )
        CREATE INDEX [IX_UserGroups_UserId] ON [Groups].[UserGroups]([UserId]);

    IF COL_LENGTH('Groups.UserGroups', 'Status') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1 FROM sys.indexes
           WHERE object_id = OBJECT_ID(N'[Groups].[UserGroups]')
             AND name = N'IX_UserGroups_GroupId_Status'
       )
        CREATE INDEX [IX_UserGroups_GroupId_Status] ON [Groups].[UserGroups]([GroupId], [Status]);

    -- FKs
    IF OBJECT_ID(N'[Groups].[Groups]', N'U') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1 FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'[Groups].[UserGroups]')
             AND name = N'FK_UserGroups_Groups_GroupId'
       )
    BEGIN
        DECLARE @groupsPkCol sysname = NULL;
        IF COL_LENGTH('Groups.Groups', 'GroupId') IS NOT NULL SET @groupsPkCol = N'GroupId';
        IF @groupsPkCol IS NULL AND COL_LENGTH('Groups.Groups', 'Id') IS NOT NULL SET @groupsPkCol = N'Id';

        IF @groupsPkCol IS NOT NULL
        BEGIN
            DECLARE @fkGroupsSql nvarchar(max) =
                N'ALTER TABLE [Groups].[UserGroups] WITH CHECK ' +
                N'ADD CONSTRAINT [FK_UserGroups_Groups_GroupId] ' +
                N'FOREIGN KEY ([GroupId]) REFERENCES [Groups].[Groups](' + QUOTENAME(@groupsPkCol) + N') ON DELETE CASCADE;';
            EXEC sp_executesql @fkGroupsSql;
        END
    END

    IF OBJECT_ID(N'[Users].[AspNetUsers]', N'U') IS NOT NULL
       AND NOT EXISTS (
           SELECT 1 FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'[Groups].[UserGroups]')
             AND name = N'FK_UserGroups_AspNetUsers_UserId'
       )
    BEGIN
        ALTER TABLE [Groups].[UserGroups] WITH CHECK
            ADD CONSTRAINT [FK_UserGroups_AspNetUsers_UserId]
                FOREIGN KEY ([UserId]) REFERENCES [Users].[AspNetUsers]([Id]) ON DELETE CASCADE;
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
