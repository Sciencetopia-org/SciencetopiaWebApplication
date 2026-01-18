using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class updateGroupCohortPlanDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cohorts",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "LessonTagAssignments",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "StudyGroupUserRoles",
                schema: "StudyGroups");

            migrationBuilder.RenameColumn(
                name: "PlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "ActivePlanVersionId");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RelationType",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Adopt");

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DescriptionL10nSetId",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JoinPolicy",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Request");

            migrationBuilder.AddColumn<Guid>(
                name: "NameL10nSetId",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Visibility",
                schema: "StudyGroups",
                table: "StudyGroups",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Private");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "StudyPlans",
                table: "Lessons",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Reading");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                schema: "StudyPlans",
                table: "Lessons",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                schema: "StudyPlans",
                table: "Lessons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "StudyPlanId",
                schema: "StudyPlans",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GroupMembers",
                schema: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Active"),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    LeftAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupMembers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "Users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupMembers_StudyGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "StudyGroups",
                        principalTable: "StudyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupPlanSwitches",
                schema: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FromVersionNumber = table.Column<int>(type: "int", nullable: true),
                    ToVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToVersionNumber = table.Column<int>(type: "int", nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Actor = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    Reason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPlanSwitches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                schema: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                });

            migrationBuilder.Sql(
                @"INSERT INTO [StudyGroups].[Groups] ([Id], [Kind])
                  SELECT sg.[Id], 'StudyGroup'
                  FROM [StudyGroups].[StudyGroups] sg
                  WHERE NOT EXISTS (
                      SELECT 1 FROM [StudyGroups].[Groups] g WHERE g.[Id] = sg.[Id]
                  );");

            migrationBuilder.CreateTable(
                name: "UserStudyPlanEnrollments",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CohortGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Learner"),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "Active"),
                    ProgressJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStudyPlanEnrollments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cohorts",
                schema: "StudyGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudyPlanVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, defaultValue: "private"),
                    NameL10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EnrollMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "OptIn"),
                    PinnedVersionNumber = table.Column<int>(type: "int", nullable: true),
                    MembersCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cohorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cohorts_Groups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "StudyGroups",
                        principalTable: "Groups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Cohorts_Groups_Id",
                        column: x => x.Id,
                        principalSchema: "StudyGroups",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Cohorts_Groups_StudyGroupId",
                        column: x => x.StudyGroupId,
                        principalSchema: "StudyGroups",
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SGSP_RelationType",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                sql: "RelationType IN ('Offer','Adopt','Default')");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroups_Visibility",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "Visibility");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_StudyPlanId",
                schema: "StudyPlans",
                table: "Lessons",
                column: "StudyPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_StudyPlanId_StableId",
                schema: "StudyPlans",
                table: "Lessons",
                columns: new[] { "StudyPlanId", "StableId" },
                unique: true,
                filter: "[StudyPlanId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_GroupId",
                schema: "StudyGroups",
                table: "Cohorts",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyGroupId",
                schema: "StudyGroups",
                table: "Cohorts",
                column: "StudyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyPlanStableId",
                schema: "StudyGroups",
                table: "Cohorts",
                column: "StudyPlanStableId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyPlanStableId_PinnedVersionNumber",
                schema: "StudyGroups",
                table: "Cohorts",
                columns: new[] { "StudyPlanStableId", "PinnedVersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_GroupId_UserId",
                schema: "StudyGroups",
                table: "GroupMembers",
                columns: new[] { "GroupId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupMembers_UserId",
                schema: "StudyGroups",
                table: "GroupMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupPlanSwitches_GroupPlan_Effective",
                schema: "StudyGroups",
                table: "GroupPlanSwitches",
                columns: new[] { "StudyGroupId", "PlanStableId", "EffectiveAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Kind",
                schema: "StudyGroups",
                table: "Groups",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_UserStudyPlanEnrollments_CohortGroupId",
                schema: "StudyPlans",
                table: "UserStudyPlanEnrollments",
                column: "CohortGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStudyPlanEnrollments_GroupId",
                schema: "StudyPlans",
                table: "UserStudyPlanEnrollments",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_UserStudyPlanEnrollments_UserId_StudyPlanStableId",
                schema: "StudyPlans",
                table: "UserStudyPlanEnrollments",
                columns: new[] { "UserId", "StudyPlanStableId" });

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Groups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "GroupId",
                principalSchema: "StudyGroups",
                principalTable: "Groups",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_StudyGroups_Groups_Id",
                schema: "StudyGroups",
                table: "StudyGroups",
                column: "Id",
                principalSchema: "StudyGroups",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Groups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropForeignKey(
                name: "FK_StudyGroups_Groups_Id",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropTable(
                name: "Cohorts",
                schema: "StudyGroups");

            migrationBuilder.DropTable(
                name: "GroupMembers",
                schema: "StudyGroups");

            migrationBuilder.DropTable(
                name: "GroupPlanSwitches",
                schema: "StudyGroups");

            migrationBuilder.DropTable(
                name: "UserStudyPlanEnrollments",
                schema: "StudyPlans");

            migrationBuilder.DropTable(
                name: "Groups",
                schema: "StudyGroups");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SGSP_RelationType",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_StudyGroups_Visibility",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_StudyPlanId",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_StudyPlanId_StableId",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "RelationType",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans");

            migrationBuilder.DropColumn(
                name: "DescriptionL10nSetId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "GroupId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "JoinPolicy",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "NameL10nSetId",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "Visibility",
                schema: "StudyGroups",
                table: "StudyGroups");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "OrderIndex",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "StudyPlanId",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.RenameColumn(
                name: "ActivePlanVersionId",
                schema: "StudyGroups",
                table: "StudyGroupStudyPlans",
                newName: "PlanVersionId");

            migrationBuilder.CreateTable(
                name: "Cohorts",
                schema: "StudyPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true, collation: "SQL_Latin1_General_CP1_CI_AS"),
                    EndAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnrollMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, defaultValue: "OptIn"),
                    MembersCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    PinnedVersionNumber = table.Column<int>(type: "int", nullable: true),
                    StartAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StudyGroupId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StudyPlanStableId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Visibility = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, defaultValue: "private")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cohorts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cohorts_StudyGroups_StudyGroupId",
                        column: x => x.StudyGroupId,
                        principalSchema: "StudyGroups",
                        principalTable: "StudyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

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
                name: "StudyGroupUserRoles",
                schema: "StudyGroups",
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
                        principalSchema: "Users",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudyGroupUserRoles_StudyGroups_GroupId",
                        column: x => x.GroupId,
                        principalSchema: "StudyGroups",
                        principalTable: "StudyGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyGroupId",
                schema: "StudyPlans",
                table: "Cohorts",
                column: "StudyGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyPlanStableId",
                schema: "StudyPlans",
                table: "Cohorts",
                column: "StudyPlanStableId");

            migrationBuilder.CreateIndex(
                name: "IX_Cohorts_StudyPlanStableId_PinnedVersionNumber",
                schema: "StudyPlans",
                table: "Cohorts",
                columns: new[] { "StudyPlanStableId", "PinnedVersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonTagAssignments_TagStableId",
                schema: "StudyPlans",
                table: "LessonTagAssignments",
                column: "TagStableId");

            migrationBuilder.CreateIndex(
                name: "IX_StudyGroupUserRoles_UserId",
                schema: "StudyGroups",
                table: "StudyGroupUserRoles",
                column: "UserId");
        }
    }
}
