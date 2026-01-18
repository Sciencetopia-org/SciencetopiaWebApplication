using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class EditDailySummariesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalKnowledgeNodeViews",
                table: "DailySummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalKnowledgeNodes",
                table: "DailySummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalStudyGroups",
                table: "DailySummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyActiveStudyGroups",
                table: "DailySummaries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalKnowledgeNodeViews",
                table: "DailySummaries");

            migrationBuilder.DropColumn(
                name: "TotalKnowledgeNodes",
                table: "DailySummaries");

            migrationBuilder.DropColumn(
                name: "TotalStudyGroups",
                table: "DailySummaries");

            migrationBuilder.DropColumn(
                name: "WeeklyActiveStudyGroups",
                table: "DailySummaries");
        }
    }
}
