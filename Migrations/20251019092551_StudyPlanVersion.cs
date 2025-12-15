using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class StudyPlanVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LockfileJson",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ResolveToHead",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LockfileJson",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "PlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "ResolveToHead",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");
        }
    }
}
