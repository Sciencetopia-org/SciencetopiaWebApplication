using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SciencetopiaWebApplication.Migrations
{
    public partial class FixMessagingAndPrivacy : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Add Privacy to StudyPlans if missing
            try
            {
                migrationBuilder.AddColumn<string>(
                    name: "Privacy",
                    table: "StudyPlans",
                    type: "nvarchar(16)",
                    maxLength: 16,
                    nullable: false,
                    defaultValue: "private");

                migrationBuilder.CreateIndex(
                    name: "IX_StudyPlans_Privacy",
                    table: "StudyPlans",
                    column: "Privacy");
            }
            catch { /* Column or index may already exist in some environments. */ }

            // 2) Convert Conversations/Notifications/Messages PKs and FKs from nvarchar(450) to uniqueidentifier
            // Drop FK and index that depend on Messages.ConversationId (if they exist)
            try
            {
                migrationBuilder.DropForeignKey(
                    name: "FK_Messages_Conversations_ConversationId",
                    table: "Messages");
            }
            catch { }

            try
            {
                migrationBuilder.DropIndex(
                    name: "IX_Messages_ConversationId",
                    table: "Messages");
            }
            catch { }

            // Add temporary columns with the target type
            try { migrationBuilder.AddColumn<Guid>(name: "Id_new", table: "Conversations", type: "uniqueidentifier", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<Guid>(name: "Id_new", table: "Messages", type: "uniqueidentifier", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<Guid>(name: "ConversationId_new", table: "Messages", type: "uniqueidentifier", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<Guid>(name: "Id_new", table: "Notifications", type: "uniqueidentifier", nullable: true); } catch { }

            // Populate temporary columns by converting from existing string values
            // If conversion fails, generate new GUIDs to keep data consistent
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Conversations'))
                BEGIN
                    UPDATE Conversations SET Id_new = TRY_CONVERT(uniqueidentifier, Id);
                    UPDATE Conversations SET Id_new = NEWID() WHERE Id_new IS NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Messages'))
                BEGIN
                    UPDATE Messages SET Id_new = TRY_CONVERT(uniqueidentifier, Id);
                    UPDATE Messages SET Id_new = NEWID() WHERE Id_new IS NULL;
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ConversationId' AND Object_ID = Object_ID('Messages'))
                BEGIN
                    UPDATE Messages SET ConversationId_new = TRY_CONVERT(uniqueidentifier, ConversationId);
                END
            ");

            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Notifications'))
                BEGIN
                    UPDATE Notifications SET Id_new = TRY_CONVERT(uniqueidentifier, Id);
                    UPDATE Notifications SET Id_new = NEWID() WHERE Id_new IS NULL;
                END
            ");

            // Drop PKs to allow column replacement
            try { migrationBuilder.DropPrimaryKey(name: "PK_Messages", table: "Messages"); } catch { }
            try { migrationBuilder.DropPrimaryKey(name: "PK_Conversations", table: "Conversations"); } catch { }
            try { migrationBuilder.DropPrimaryKey(name: "PK_Notifications", table: "Notifications"); } catch { }

            // Drop old columns and rename new ones
            try
            {
                migrationBuilder.DropColumn(name: "Id", table: "Conversations");
                migrationBuilder.RenameColumn(name: "Id_new", table: "Conversations", newName: "Id");
            }
            catch { }

            try
            {
                migrationBuilder.DropColumn(name: "Id", table: "Messages");
                migrationBuilder.RenameColumn(name: "Id_new", table: "Messages", newName: "Id");
            }
            catch { }

            try
            {
                migrationBuilder.DropColumn(name: "ConversationId", table: "Messages");
                migrationBuilder.RenameColumn(name: "ConversationId_new", table: "Messages", newName: "ConversationId");
            }
            catch { }

            try
            {
                migrationBuilder.DropColumn(name: "Id", table: "Notifications");
                migrationBuilder.RenameColumn(name: "Id_new", table: "Notifications", newName: "Id");
            }
            catch { }

            // Recreate PKs
            try { migrationBuilder.AddPrimaryKey(name: "PK_Conversations", table: "Conversations", column: "Id"); } catch { }
            try { migrationBuilder.AddPrimaryKey(name: "PK_Messages", table: "Messages", column: "Id"); } catch { }
            try { migrationBuilder.AddPrimaryKey(name: "PK_Notifications", table: "Notifications", column: "Id"); } catch { }

            // Recreate index and FK for Messages.ConversationId
            try
            {
                migrationBuilder.CreateIndex(
                    name: "IX_Messages_ConversationId",
                    table: "Messages",
                    column: "ConversationId");

                migrationBuilder.AddForeignKey(
                    name: "FK_Messages_Conversations_ConversationId",
                    table: "Messages",
                    column: "ConversationId",
                    principalTable: "Conversations",
                    principalColumn: "Id");
            }
            catch { }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort rollback
            // 1) Drop Privacy index/column if exists
            try { migrationBuilder.DropIndex(name: "IX_StudyPlans_Privacy", table: "StudyPlans"); } catch { }
            try { migrationBuilder.DropColumn(name: "Privacy", table: "StudyPlans"); } catch { }

            // 2) Revert GUID columns to strings (nvarchar(450)) for Conversations/Messages/Notifications
            try { migrationBuilder.DropForeignKey(name: "FK_Messages_Conversations_ConversationId", table: "Messages"); } catch { }
            try { migrationBuilder.DropIndex(name: "IX_Messages_ConversationId", table: "Messages"); } catch { }

            // Add temp string columns
            try { migrationBuilder.AddColumn<string>(name: "Id_old", table: "Conversations", type: "nvarchar(450)", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<string>(name: "Id_old", table: "Messages", type: "nvarchar(450)", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<string>(name: "ConversationId_old", table: "Messages", type: "nvarchar(450)", nullable: true); } catch { }
            try { migrationBuilder.AddColumn<string>(name: "Id_old", table: "Notifications", type: "nvarchar(450)", nullable: true); } catch { }

            // Copy data back as strings
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Conversations'))
                BEGIN
                    UPDATE Conversations SET Id_old = CONVERT(nvarchar(450), Id);
                END
            ");
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Messages'))
                BEGIN
                    UPDATE Messages SET Id_old = CONVERT(nvarchar(450), Id);
                END
            ");
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'ConversationId' AND Object_ID = Object_ID('Messages'))
                BEGIN
                    UPDATE Messages SET ConversationId_old = CONVERT(nvarchar(450), ConversationId);
                END
            ");
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.columns WHERE Name = 'Id' AND Object_ID = Object_ID('Notifications'))
                BEGIN
                    UPDATE Notifications SET Id_old = CONVERT(nvarchar(450), Id);
                END
            ");

            // Replace columns
            try { migrationBuilder.DropPrimaryKey(name: "PK_Messages", table: "Messages"); } catch { }
            try { migrationBuilder.DropPrimaryKey(name: "PK_Conversations", table: "Conversations"); } catch { }
            try { migrationBuilder.DropPrimaryKey(name: "PK_Notifications", table: "Notifications"); } catch { }

            try { migrationBuilder.DropColumn(name: "Id", table: "Conversations"); migrationBuilder.RenameColumn(name: "Id_old", table: "Conversations", newName: "Id"); } catch { }
            try { migrationBuilder.DropColumn(name: "Id", table: "Messages"); migrationBuilder.RenameColumn(name: "Id_old", table: "Messages", newName: "Id"); } catch { }
            try { migrationBuilder.DropColumn(name: "ConversationId", table: "Messages"); migrationBuilder.RenameColumn(name: "ConversationId_old", table: "Messages", newName: "ConversationId"); } catch { }
            try { migrationBuilder.DropColumn(name: "Id", table: "Notifications"); migrationBuilder.RenameColumn(name: "Id_old", table: "Notifications", newName: "Id"); } catch { }

            // Restore PKs
            try { migrationBuilder.AddPrimaryKey(name: "PK_Conversations", table: "Conversations", column: "Id"); } catch { }
            try { migrationBuilder.AddPrimaryKey(name: "PK_Messages", table: "Messages", column: "Id"); } catch { }
            try { migrationBuilder.AddPrimaryKey(name: "PK_Notifications", table: "Notifications", column: "Id"); } catch { }

            // Restore index and FK
            try
            {
                migrationBuilder.CreateIndex(name: "IX_Messages_ConversationId", table: "Messages", column: "ConversationId");
                migrationBuilder.AddForeignKey(
                    name: "FK_Messages_Conversations_ConversationId",
                    table: "Messages",
                    column: "ConversationId",
                    principalTable: "Conversations",
                    principalColumn: "Id");
            }
            catch { }
        }
    }
}

