using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class Step1_AddPlanLessonVersionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "StableId",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                schema: "StudyPlans",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "StudyPlans",
                table: "Lessons",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "StudyPlans",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "StudyPlans",
                table: "Lessons",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                schema: "StudyPlans",
                table: "Lessons",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAt",
                schema: "StudyPlans",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                schema: "StudyPlans",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "StudyPlans",
                table: "Lessons",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "StableId",
                schema: "StudyPlans",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "StudyPlans",
                table: "Lessons",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Draft");

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "Lessons",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(@"
UPDATE sp
SET StableId = CASE WHEN StableId = '00000000-0000-0000-0000-000000000000' THEN Id ELSE StableId END,
    VersionNumber = CASE WHEN VersionNumber = 0 THEN 1 ELSE VersionNumber END,
    Status = CASE WHEN Status IS NULL OR LTRIM(RTRIM(Status)) = '' THEN 'Current' ELSE Status END,
    IsCurrent = 1,
    CreatedAt = COALESCE(CreatedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00')),
    PublishedAt = COALESCE(PublishedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00')),
    ApprovedAt = COALESCE(ApprovedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00')),
    CreatedBy = COALESCE(CreatedBy, CAST(CreatorId AS nvarchar(128))),
    ApprovedBy = COALESCE(ApprovedBy, COALESCE(CreatedBy, CAST(CreatorId AS nvarchar(128))))
FROM [StudyPlans].[StudyPlans] sp;

UPDATE l
SET StableId = CASE WHEN StableId = '00000000-0000-0000-0000-000000000000' THEN Id ELSE StableId END,
    VersionNumber = CASE WHEN VersionNumber = 0 THEN 1 ELSE VersionNumber END,
    Status = CASE WHEN Status IS NULL OR LTRIM(RTRIM(Status)) = '' THEN 'Current' ELSE Status END,
    IsCurrent = 1,
    CreatedAt = COALESCE(CreatedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00')),
    PublishedAt = COALESCE(PublishedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00')),
    ApprovedAt = COALESCE(ApprovedAt, SWITCHOFFSET(CONVERT(datetimeoffset(7), CreatedDate), '+00:00'))
FROM [StudyPlans].[Lessons] l;
");

            migrationBuilder.AlterColumn<Guid>(
                name: "StableId",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()",
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<int>(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "StudyPlans",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<Guid>(
                name: "StableId",
                schema: "StudyPlans",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: false,
                defaultValueSql: "NEWID()",
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldDefaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AlterColumn<int>(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "Lessons",
                type: "int",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_SP_Stable_Version",
                schema: "StudyPlans",
                table: "StudyPlans",
                columns: new[] { "StableId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_SP_Stable_Current",
                schema: "StudyPlans",
                table: "StudyPlans",
                column: "StableId",
                unique: true,
                filter: "[IsCurrent] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Lesson_Stable_Version",
                schema: "StudyPlans",
                table: "Lessons",
                columns: new[] { "StableId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "UX_Lesson_Stable_Current",
                schema: "StudyPlans",
                table: "Lessons",
                column: "StableId",
                unique: true,
                filter: "[IsCurrent] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SP_Stable_Version",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "UX_SP_Stable_Current",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropIndex(
                name: "IX_Lesson_Stable_Version",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "UX_Lesson_Stable_Current",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "StableId",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "StudyPlans");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "PublishedAt",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "StableId",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "StudyPlans",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                schema: "StudyPlans",
                table: "Lessons");
        }
    }
}
