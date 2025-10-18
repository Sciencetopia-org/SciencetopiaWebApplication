using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class updateVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LessonTagAssignments",
                schema: "StudyPlans",
                columns: table => new
                {
                    LessonStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonVersionNumber = table.Column<int>(type: "int", nullable: false),
                    TagStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonTagAssignments", x => new { x.LessonStableId, x.LessonVersionNumber, x.TagStableId });
                });

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
                name: "IX_LessonTagAssignments_TagStableId",
                schema: "StudyPlans",
                table: "LessonTagAssignments",
                column: "TagStableId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanTagAssignments_TagStableId",
                schema: "StudyPlans",
                table: "StudyPlanTagAssignments",
                column: "TagStableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonTagAssignments",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "StudyPlanTagAssignments",
                schema: "StudyPlans");
        }
    }
}
