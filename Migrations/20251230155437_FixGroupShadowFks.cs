using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class FixGroupShadowFks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cohorts_Groups_GroupId",
                schema: "StudyGroups",
                table: "Cohorts");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Groups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_Cohorts_GroupId",
                schema: "StudyGroups",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "StudyGroups",
                table: "Cohorts");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "StudyGroups",
                table: "Cohorts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_GroupId",
                schema: "StudyGroups",
                table: "Cohorts",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cohorts_Groups_GroupId",
                schema: "StudyGroups",
                table: "Cohorts",
                column: "GroupId",
                principalSchema: "StudyGroups",
                principalTable: "Groups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Groups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "GroupId",
                principalSchema: "StudyGroups",
                principalTable: "Groups",
                principalColumn: "Id");
        }
    }
}
