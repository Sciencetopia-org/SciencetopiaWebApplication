using Microsoft.EntityFrameworkCore.Migrations;
using System;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class ConvertMessagingAndPrivacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add Privacy to StudyPlans with default and index (idempotent)
            migrationBuilder.Sql(@"
                IF COL_LENGTH('StudyPlans', 'Privacy') IS NULL
                BEGIN
                    ALTER TABLE StudyPlans ADD Privacy nvarchar(16) NULL CONSTRAINT DF_StudyPlans_Privacy DEFAULT N'private';
                END
                ELSE
                BEGIN
                    DECLARE @df nvarchar(128);
                    SELECT @df = d.name
                    FROM sys.default_constraints d
                    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                    WHERE d.parent_object_id = OBJECT_ID('StudyPlans') AND c.name = 'Privacy';
                    IF @df IS NULL
                    BEGIN
                        ALTER TABLE StudyPlans ADD DEFAULT N'private' FOR Privacy;
                    END
                END
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_StudyPlans_Privacy' AND object_id = OBJECT_ID('StudyPlans'))
                BEGIN
                    CREATE INDEX IX_StudyPlans_Privacy ON StudyPlans(Privacy);
                END
            ");

            // 2) Messaging: convert key columns from nvarchar to uniqueidentifier (idempotent)
            // Drop FK and index if exist
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Messages_Conversations_ConversationId')
                BEGIN
                    ALTER TABLE Messages DROP CONSTRAINT FK_Messages_Conversations_ConversationId;
                END
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Messages_ConversationId' AND object_id = OBJECT_ID('Messages'))
                BEGIN
                    DROP INDEX IX_Messages_ConversationId ON Messages;
                END
            ");

            // Conversations.Id conversion (stepwise, idempotent)
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'Id' AND system_type_id IN (167,231)) AND COL_LENGTH('Conversations','Id_new') IS NULL ALTER TABLE Conversations ADD Id_new uniqueidentifier NULL;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Conversations','Id_new') IS NOT NULL EXEC('UPDATE Conversations SET Id_new = TRY_CONVERT(uniqueidentifier, Id) WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Conversations','Id_new') IS NOT NULL EXEC('UPDATE Conversations SET Id_new = NEWID() WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Conversations','Id_new') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Conversations') ALTER TABLE Conversations DROP CONSTRAINT PK_Conversations; END");
            migrationBuilder.Sql(@"IF COL_LENGTH('Conversations','Id_new') IS NOT NULL ALTER TABLE Conversations DROP COLUMN Id;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Conversations','Id_new') IS NOT NULL EXEC sp_rename 'Conversations.Id_new', 'Id', 'COLUMN';");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'Id' AND system_type_id = 36) ALTER TABLE Conversations ALTER COLUMN Id uniqueidentifier NOT NULL;");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'Id' AND system_type_id = 36) AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Conversations') ALTER TABLE Conversations ADD CONSTRAINT PK_Conversations PRIMARY KEY (Id);");

            // Messages.Id conversion
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Id' AND system_type_id IN (167,231)) AND COL_LENGTH('Messages','Id_new') IS NULL ALTER TABLE Messages ADD Id_new uniqueidentifier NULL;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','Id_new') IS NOT NULL EXEC('UPDATE Messages SET Id_new = TRY_CONVERT(uniqueidentifier, Id) WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','Id_new') IS NOT NULL EXEC('UPDATE Messages SET Id_new = NEWID() WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','Id_new') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Messages') ALTER TABLE Messages DROP CONSTRAINT PK_Messages; END");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','Id_new') IS NOT NULL ALTER TABLE Messages DROP COLUMN Id;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','Id_new') IS NOT NULL EXEC sp_rename 'Messages.Id_new', 'Id', 'COLUMN';");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Id' AND system_type_id = 36) ALTER TABLE Messages ALTER COLUMN Id uniqueidentifier NOT NULL;");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Id' AND system_type_id = 36) AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Messages') ALTER TABLE Messages ADD CONSTRAINT PK_Messages PRIMARY KEY (Id);");

            // Messages.ConversationId conversion
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'ConversationId' AND system_type_id IN (167,231)) AND COL_LENGTH('Messages','ConversationId_new') IS NULL ALTER TABLE Messages ADD ConversationId_new uniqueidentifier NULL;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','ConversationId_new') IS NOT NULL EXEC('UPDATE Messages SET ConversationId_new = TRY_CONVERT(uniqueidentifier, ConversationId) WHERE ConversationId_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','ConversationId_new') IS NOT NULL ALTER TABLE Messages DROP COLUMN ConversationId;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Messages','ConversationId_new') IS NOT NULL EXEC sp_rename 'Messages.ConversationId_new', 'ConversationId', 'COLUMN';");

            migrationBuilder.Sql(@"IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Messages_ConversationId' AND object_id = OBJECT_ID('Messages')) CREATE INDEX IX_Messages_ConversationId ON Messages(ConversationId);");
            migrationBuilder.Sql(@"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Messages_Conversations_ConversationId') ALTER TABLE Messages ADD CONSTRAINT FK_Messages_Conversations_ConversationId FOREIGN KEY (ConversationId) REFERENCES Conversations(Id);");

            // Notifications.Id conversion
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Notifications') AND name = 'Id' AND system_type_id IN (167,231)) AND COL_LENGTH('Notifications','Id_new') IS NULL ALTER TABLE Notifications ADD Id_new uniqueidentifier NULL;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Notifications','Id_new') IS NOT NULL EXEC('UPDATE Notifications SET Id_new = TRY_CONVERT(uniqueidentifier, Id) WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Notifications','Id_new') IS NOT NULL EXEC('UPDATE Notifications SET Id_new = NEWID() WHERE Id_new IS NULL;');");
            migrationBuilder.Sql(@"IF COL_LENGTH('Notifications','Id_new') IS NOT NULL BEGIN IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Notifications') ALTER TABLE Notifications DROP CONSTRAINT PK_Notifications; END");
            migrationBuilder.Sql(@"IF COL_LENGTH('Notifications','Id_new') IS NOT NULL ALTER TABLE Notifications DROP COLUMN Id;");
            migrationBuilder.Sql(@"IF COL_LENGTH('Notifications','Id_new') IS NOT NULL EXEC sp_rename 'Notifications.Id_new', 'Id', 'COLUMN';");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Notifications') AND name = 'Id' AND system_type_id = 36) ALTER TABLE Notifications ALTER COLUMN Id uniqueidentifier NOT NULL;");
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Notifications') AND name = 'Id' AND system_type_id = 36) AND NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Notifications') ALTER TABLE Notifications ADD CONSTRAINT PK_Notifications PRIMARY KEY (Id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort down migration
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Messages_Conversations_ConversationId')
                    ALTER TABLE Messages DROP CONSTRAINT FK_Messages_Conversations_ConversationId;

                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Messages_ConversationId' AND object_id = OBJECT_ID('Messages'))
                    DROP INDEX IX_Messages_ConversationId ON Messages;

                IF COL_LENGTH('StudyPlans', 'Privacy') IS NOT NULL
                BEGIN
                    DECLARE @df nvarchar(128);
                    SELECT @df = d.name
                    FROM sys.default_constraints d
                    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
                    WHERE d.parent_object_id = OBJECT_ID('StudyPlans') AND c.name = 'Privacy';
                    IF @df IS NOT NULL EXEC('ALTER TABLE StudyPlans DROP CONSTRAINT ' + @df);
                    ALTER TABLE StudyPlans DROP COLUMN Privacy;
                END

                -- Recreate string columns if needed (Id_old pattern)
                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Conversations') AND name = 'Id' AND system_type_id = 36)
                BEGIN
                    ALTER TABLE Conversations DROP CONSTRAINT PK_Conversations;
                    ALTER TABLE Conversations ADD Id_old nvarchar(450) NULL;
                    UPDATE Conversations SET Id_old = CONVERT(nvarchar(450), Id);
                    ALTER TABLE Conversations DROP COLUMN Id;
                    EXEC sp_rename 'Conversations.Id_old', 'Id', 'COLUMN';
                    ALTER TABLE Conversations ADD CONSTRAINT PK_Conversations PRIMARY KEY (Id);
                END

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'Id' AND system_type_id = 36)
                BEGIN
                    ALTER TABLE Messages DROP CONSTRAINT PK_Messages;
                    ALTER TABLE Messages ADD Id_old nvarchar(450) NULL;
                    UPDATE Messages SET Id_old = CONVERT(nvarchar(450), Id);
                    ALTER TABLE Messages DROP COLUMN Id;
                    EXEC sp_rename 'Messages.Id_old', 'Id', 'COLUMN';
                    ALTER TABLE Messages ADD CONSTRAINT PK_Messages PRIMARY KEY (Id);
                END

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Messages') AND name = 'ConversationId' AND system_type_id = 36)
                BEGIN
                    ALTER TABLE Messages ADD ConversationId_old nvarchar(450) NULL;
                    UPDATE Messages SET ConversationId_old = CONVERT(nvarchar(450), ConversationId);
                    ALTER TABLE Messages DROP COLUMN ConversationId;
                    EXEC sp_rename 'Messages.ConversationId_old', 'ConversationId', 'COLUMN';
                END

                IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('Notifications') AND name = 'Id' AND system_type_id = 36)
                BEGIN
                    ALTER TABLE Notifications DROP CONSTRAINT PK_Notifications;
                    ALTER TABLE Notifications ADD Id_old nvarchar(450) NULL;
                    UPDATE Notifications SET Id_old = CONVERT(nvarchar(450), Id);
                    ALTER TABLE Notifications DROP COLUMN Id;
                    EXEC sp_rename 'Notifications.Id_old', 'Id', 'COLUMN';
                    ALTER TABLE Notifications ADD CONSTRAINT PK_Notifications PRIMARY KEY (Id);
                END
            ");
        }
    }
}
