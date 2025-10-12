using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotArchitectureRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Cohorts_StudyPlans_StudyPlanId",
                schema: "StudyPlans",
                table: "Cohorts");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroupStudyPlans_StudyPlans_StudyPlanId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyPlanUserRoles_StudyPlans_PlanId",
                schema: "StudyPlans",
                table: "StudyPlanUserRoles");

            migrationBuilder.DropTable(
                name: "LessonDrafts",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "LessonVersions",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "StudyPlanDrafts",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "StudyPlanVersions",
                schema: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "CurrentVersionId",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "UseDraftFlow",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "ActivePlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "PinnedVersionId",
                schema: "StudyPlans",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "ActivePlanVersionId",
                schema: "StudyPlans",
                table: "Cohorts");

            migrationBuilder.RenameColumn(
                name: "PlanId",
                schema: "StudyPlans",
                table: "StudyPlanUserRoles",
                newName: "PlanStableId");

            migrationBuilder.RenameColumn(
                name: "StudyPlanId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "StudyPlanStableId");

            migrationBuilder.RenameIndex(
                name: "IX_StudyGroupStudyPlans_StudyPlanId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "IX_StudyGroupStudyPlans_StudyPlanStableId");

            migrationBuilder.RenameIndex(
                name: "IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanStableId");

            migrationBuilder.RenameColumn(
                name: "StudyPlanId",
                schema: "StudyPlans",
                table: "Cohorts",
                newName: "StudyPlanStableId");

            migrationBuilder.RenameIndex(
                name: "IX_Cohorts_StudyPlanId",
                schema: "StudyPlans",
                table: "Cohorts",
                newName: "IX_Cohorts_StudyPlanStableId");

            migrationBuilder.AddColumn<int>(
                name: "PinnedVersionNumber",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PinnedVersionNumber",
                schema: "StudyPlans",
                table: "Cohorts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StudyPlanLessonSnapshots",
                schema: "StudyPlans",
                columns: table => new
                {
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanVersionNumber = table.Column<int>(type: "int", nullable: false),
                    LessonStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonVersionNumber = table.Column<int>(type: "int", nullable: false),
                    StepType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanLessonSnapshots", x => new { x.StudyPlanStableId, x.StudyPlanVersionNumber, x.LessonStableId, x.LessonVersionNumber });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyPlanStableId_PinnedVersionNumber",
                schema: "StudyPlans",
                table: "Cohorts",
                columns: new[] { "StudyPlanStableId", "PinnedVersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanLessonSnapshots_LessonStableId_LessonVersionNumber",
                schema: "StudyPlans",
                table: "StudyPlanLessonSnapshots",
                columns: new[] { "LessonStableId", "LessonVersionNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudyPlanLessonSnapshots",
                schema: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_Cohorts_StudyPlanStableId_PinnedVersionNumber",
                schema: "StudyPlans",
                table: "Cohorts");

            migrationBuilder.DropColumn(
                name: "PinnedVersionNumber",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "PinnedVersionNumber",
                schema: "StudyPlans",
                table: "Cohorts");

            migrationBuilder.RenameColumn(
                name: "PlanStableId",
                schema: "StudyPlans",
                table: "StudyPlanUserRoles",
                newName: "PlanId");

            migrationBuilder.RenameColumn(
                name: "StudyPlanStableId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "StudyPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_StudyGroupStudyPlans_StudyPlanStableId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "IX_StudyGroupStudyPlans_StudyPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanStableId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "IX_StudyGroupStudyPlans_StudyGroupId_StudyPlanId");

            migrationBuilder.RenameColumn(
                name: "StudyPlanStableId",
                schema: "StudyPlans",
                table: "Cohorts",
                newName: "StudyPlanId");

            migrationBuilder.RenameIndex(
                name: "IX_Cohorts_StudyPlanStableId",
                schema: "StudyPlans",
                table: "Cohorts",
                newName: "IX_Cohorts_StudyPlanId");

            migrationBuilder.AddColumn<long>(
                name: "CurrentVersionId",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UseDraftFlow",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ActivePlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<long>(
                name: "PinnedVersionId",
                schema: "StudyPlans",
                table: "Cohorts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ActivePlanVersionId",
                schema: "StudyPlans",
                table: "Cohorts",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "LessonDrafts",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftNumber = table.Column<int>(type: "int", nullable: false),
                    DraftStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonDrafts_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalSchema: "StudyPlans",
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonVersions",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChangeNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonVersions_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalSchema: "StudyPlans",
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanDrafts",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DraftNumber = table.Column<int>(type: "int", nullable: false),
                    DraftStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StudyPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPlanDrafts_StudyPlans_StudyPlanId",
                        column: x => x.StudyPlanId,
                        principalSchema: "StudyPlans",
                        principalTable: "StudyPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudyPlanVersions",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ChangeNotes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StudyPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudyPlanVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudyPlanVersions_StudyPlans_StudyPlanId",
                        column: x => x.StudyPlanId,
                        principalSchema: "StudyPlans",
                        principalTable: "StudyPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonDrafts_LessonId_DraftNumber",
                schema: "StudyPlans",
                table: "LessonDrafts",
                columns: new[] { "LessonId", "DraftNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonVersions_LessonId_VersionNumber",
                schema: "StudyPlans",
                table: "LessonVersions",
                columns: new[] { "LessonId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanDrafts_StudyPlanId_DraftNumber",
                schema: "StudyPlans",
                table: "StudyPlanDrafts",
                columns: new[] { "StudyPlanId", "DraftNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudyPlanVersions_StudyPlanId_VersionNumber",
                schema: "StudyPlans",
                table: "StudyPlanVersions",
                columns: new[] { "StudyPlanId", "VersionNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Cohorts_StudyPlans_StudyPlanId",
                schema: "StudyPlans",
                table: "Cohorts",
                column: "StudyPlanId",
                principalSchema: "StudyPlans",
                principalTable: "StudyPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroupStudyPlans_StudyPlans_StudyPlanId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                column: "StudyPlanId",
                principalSchema: "StudyPlans",
                principalTable: "StudyPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudyPlanUserRoles_StudyPlans_PlanId",
                schema: "StudyPlans",
                table: "StudyPlanUserRoles",
                column: "PlanId",
                principalSchema: "StudyPlans",
                principalTable: "StudyPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
