using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Users");

            migrationBuilder.EnsureSchema(
                name: "StudyPlans");

            migrationBuilder.EnsureSchema(
                name: "Messages");

            migrationBuilder.EnsureSchema(
                name: "Logs");

            migrationBuilder.EnsureSchema(
                name: "KnowledgeGraph");

            migrationBuilder.EnsureSchema(
                name: "L10n");

            migrationBuilder.EnsureSchema(
                name: "StudyGroups");

            // Conditionally move tables to new schemas only if they still exist in dbo
            void MoveToSchema(string table, string schema)
            {
                migrationBuilder.Sql($@"IF OBJECT_ID(N'[dbo].[{table}]') IS NOT NULL
BEGIN
    ALTER SCHEMA [{schema}] TRANSFER [dbo].[{table}];
END");
            }

            MoveToSchema("VisitLogs", "Logs");
            MoveToSchema("DailySummaries", "Logs");

            MoveToSchema("Tags", "KnowledgeGraph");
            MoveToSchema("TypesOfTags", "KnowledgeGraph");
            MoveToSchema("TagTypes", "KnowledgeGraph");
            MoveToSchema("TagDrafts", "KnowledgeGraph");
            MoveToSchema("TagVersions", "KnowledgeGraph");
            MoveToSchema("KnowledgeNodes", "KnowledgeGraph");
            MoveToSchema("KnowledgeNodeDrafts", "KnowledgeGraph");
            MoveToSchema("KnowledgeNodeVersions", "KnowledgeGraph");
            MoveToSchema("Resources", "KnowledgeGraph");
            MoveToSchema("Favorites", "KnowledgeGraph");

            MoveToSchema("L10nSets", "L10n");
            MoveToSchema("L10nItems", "L10n");
            MoveToSchema("L10nSetItems", "L10n");
            MoveToSchema("NodeL10nSets", "L10n");
            MoveToSchema("TagL10nSets", "L10n");

            MoveToSchema("Messages", "Messages");
            MoveToSchema("Conversations", "Messages");
            MoveToSchema("Notifications", "Messages");

            MoveToSchema("StudyPlans", "StudyPlans");
            MoveToSchema("Lessons", "StudyPlans");
            MoveToSchema("LessonDrafts", "StudyPlans");
            MoveToSchema("LessonVersions", "StudyPlans");
            MoveToSchema("StudyPlanDrafts", "StudyPlans");
            MoveToSchema("StudyPlanVersions", "StudyPlans");
            MoveToSchema("StudyPlanUserRoles", "StudyPlans");
            MoveToSchema("Cohorts", "StudyPlans");

            MoveToSchema("StudyGroups", "StudyGroups");
            MoveToSchema("StudyGroupStudyPlans", "StudyGroups");
            MoveToSchema("StudyGroupUserRoles", "StudyGroups");

            MoveToSchema("AspNetUsers", "Users");
            MoveToSchema("AspNetRoles", "Users");
            MoveToSchema("AspNetUserClaims", "Users");
            MoveToSchema("AspNetUserLogins", "Users");
            MoveToSchema("AspNetUserRoles", "Users");
            MoveToSchema("AspNetUserTokens", "Users");
            MoveToSchema("AspNetRoleClaims", "Users");

            migrationBuilder.AlterColumn<string>(
                name: "FieldKey",
                schema: "L10n",
                table: "L10nItems",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "name",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldDefaultValue: "title");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "VisitLogs",
                schema: "Logs",
                newName: "VisitLogs");

            migrationBuilder.RenameTable(
                name: "TypesOfTags",
                schema: "KnowledgeGraph",
                newName: "TypesOfTags");

            migrationBuilder.RenameTable(
                name: "TagVersions",
                schema: "KnowledgeGraph",
                newName: "TagVersions");

            migrationBuilder.RenameTable(
                name: "TagTypes",
                schema: "KnowledgeGraph",
                newName: "TagTypes");

            migrationBuilder.RenameTable(
                name: "Tags",
                schema: "KnowledgeGraph",
                newName: "Tags");

            migrationBuilder.RenameTable(
                name: "TagL10nSets",
                schema: "L10n",
                newName: "TagL10nSets");

            migrationBuilder.RenameTable(
                name: "TagDrafts",
                schema: "KnowledgeGraph",
                newName: "TagDrafts");

            migrationBuilder.RenameTable(
                name: "StudyPlanVersions",
                schema: "StudyPlans",
                newName: "StudyPlanVersions");

            migrationBuilder.RenameTable(
                name: "StudyPlanUserRoles",
                schema: "StudyPlans",
                newName: "StudyPlanUserRoles");

            migrationBuilder.RenameTable(
                name: "StudyPlans",
                schema: "StudyPlans",
                newName: "StudyPlans");

            migrationBuilder.RenameTable(
                name: "StudyPlanDrafts",
                schema: "StudyPlans",
                newName: "StudyPlanDrafts");

            migrationBuilder.RenameTable(
                name: "StudyGroupUserRoles",
                schema: "StudyGroups",
                newName: "StudyGroupUserRoles");

            migrationBuilder.RenameTable(
                name: "StudyGroupStudyPlans",
                schema: "StudyGroups",
                newName: "StudyGroupStudyPlans");

            migrationBuilder.RenameTable(
                name: "StudyGroups",
                schema: "StudyGroups",
                newName: "StudyGroups");

            migrationBuilder.RenameTable(
                name: "Resources",
                schema: "KnowledgeGraph",
                newName: "Resources");

            migrationBuilder.RenameTable(
                name: "Notifications",
                schema: "Messages",
                newName: "Notifications");

            migrationBuilder.RenameTable(
                name: "NodeL10nSets",
                schema: "L10n",
                newName: "NodeL10nSets");

            migrationBuilder.RenameTable(
                name: "Messages",
                schema: "Messages",
                newName: "Messages");

            migrationBuilder.RenameTable(
                name: "LessonVersions",
                schema: "StudyPlans",
                newName: "LessonVersions");

            migrationBuilder.RenameTable(
                name: "Lessons",
                schema: "StudyPlans",
                newName: "Lessons");

            migrationBuilder.RenameTable(
                name: "LessonDrafts",
                schema: "StudyPlans",
                newName: "LessonDrafts");

            migrationBuilder.RenameTable(
                name: "L10nSets",
                schema: "L10n",
                newName: "L10nSets");

            migrationBuilder.RenameTable(
                name: "L10nSetItems",
                schema: "L10n",
                newName: "L10nSetItems");

            migrationBuilder.RenameTable(
                name: "L10nItems",
                schema: "L10n",
                newName: "L10nItems");

            migrationBuilder.RenameTable(
                name: "KnowledgeNodeVersions",
                schema: "KnowledgeGraph",
                newName: "KnowledgeNodeVersions");

            migrationBuilder.RenameTable(
                name: "KnowledgeNodes",
                schema: "KnowledgeGraph",
                newName: "KnowledgeNodes");

            migrationBuilder.RenameTable(
                name: "KnowledgeNodeDrafts",
                schema: "KnowledgeGraph",
                newName: "KnowledgeNodeDrafts");

            migrationBuilder.RenameTable(
                name: "Favorites",
                schema: "KnowledgeGraph",
                newName: "Favorites");

            migrationBuilder.RenameTable(
                name: "DailySummaries",
                schema: "Logs",
                newName: "DailySummaries");

            migrationBuilder.RenameTable(
                name: "Conversations",
                schema: "Messages",
                newName: "Conversations");

            migrationBuilder.RenameTable(
                name: "Cohorts",
                schema: "StudyPlans",
                newName: "Cohorts");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                schema: "Users",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                schema: "Users",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                schema: "Users",
                newName: "AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                schema: "Users",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                schema: "Users",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                schema: "Users",
                newName: "AspNetRoles");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                schema: "Users",
                newName: "AspNetRoleClaims");

            migrationBuilder.AlterColumn<string>(
                name: "FieldKey",
                table: "L10nItems",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "title",
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldDefaultValue: "name");
        }
    }
}
