using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class B1_EntityUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CurrentVersionId",
                table: "StudyPlans",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnrollMode",
                table: "Cohorts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "OptIn");

            migrationBuilder.AddColumn<int>(
                name: "MembersCount",
                table: "Cohorts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "PinnedVersionId",
                table: "Cohorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StudyGroupId",
                table: "Cohorts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CohortMembers",
                columns: table => new
                {
                    CohortId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CohortMembers", x => new { x.CohortId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CohortMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CohortMembers_Cohorts_CohortId",
                        column: x => x.CohortId,
                        principalTable: "Cohorts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyGroupId",
                table: "Cohorts",
                column: "StudyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_CohortMembers_UserId",
                table: "CohortMembers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Cohorts_StudyGroups_StudyGroupId",
                table: "Cohorts",
                column: "StudyGroupId",
                principalTable: "StudyGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Backfill: CurrentVersionId on StudyPlans with latest StudyPlanVersion.Id
            migrationBuilder.Sql(@"
UPDATE p
SET p.CurrentVersionId = v.Id
FROM StudyPlans p
OUTER APPLY (
    SELECT TOP 1 Id
    FROM StudyPlanVersions sv
    WHERE sv.StudyPlanId = p.Id
    ORDER BY sv.VersionNumber DESC
) v;

-- Backfill: Cohorts.PinnedVersionId = Plan.CurrentVersionId
UPDATE c
SET c.PinnedVersionId = p.CurrentVersionId
FROM Cohorts c
JOIN StudyPlans p ON p.Id = c.StudyPlanId;

-- Ensure MembersCount has a value (defaults already set)
UPDATE c SET c.MembersCount = COALESCE(c.MembersCount, 0) FROM Cohorts c; 

");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cohorts_StudyGroups_StudyGroupId",
                table: "Cohorts");

            migrationBuilder.DropTable(
                name: "CohortMembers");

            migrationBuilder.DropIndex(
                name: "IX_Cohorts_StudyGroupId",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "EnrollMode",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "MembersCount",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "PinnedVersionId",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "StudyGroupId",
                table: "Cohorts");
        }
    }
}
