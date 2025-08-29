using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanAndGroupUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudyGroupUserRoles",
                columns: table => new
                {
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Role = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyGroupUserRoles", x => new { x.GroupId, x.UserId });
                    table.ForeignKey(
                        name: "FK_StudyGroupUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudyGroupUserRoles_StudyGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "StudyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanUserRoles",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Role = table.Column<byte>(type: "tinyint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanUserRoles", x => new { x.PlanId, x.UserId });
                    table.ForeignKey(
                        name: "FK_StudyPlanUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudyPlanUserRoles_StudyPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "StudyPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupUserRoles_UserId",
                table: "StudyGroupUserRoles",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanUserRoles_UserId",
                table: "StudyPlanUserRoles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudyGroupUserRoles");

            migrationBuilder.DropTable(
                name: "StudyPlanUserRoles");
        }
    }
}
