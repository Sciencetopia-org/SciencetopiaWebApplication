using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddTagAndNodeVersionTablesAndMigratePendingDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // === 原有草稿表创建 ===
            migrationBuilder.CreateTable(
                name: "KnowledgeNodeDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodeDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeNodeDrafts_KnowledgeNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagDrafts_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodeDrafts_NodeId",
                table: "KnowledgeNodeDrafts",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TagDrafts_TagId",
                table: "TagDrafts",
                column: "TagId");

            // === ✅ 添加版本表 ===
            migrationBuilder.CreateTable(
                name: "KnowledgeNodeVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    NodeId = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(nullable: false),
                    PublishedBy = table.Column<string>(nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(nullable: true),
                    VersionNumber = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodeVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(nullable: false),
                    TagId = table.Column<Guid>(nullable: false),
                    Name = table.Column<string>(nullable: true),
                    Description = table.Column<string>(nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(nullable: false),
                    PublishedBy = table.Column<string>(nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(nullable: true),
                    VersionNumber = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagVersions", x => x.Id);
                });

            // === ✅ 将 Tags 表中 ReviewStatus = Pending 的记录迁移到 TagDrafts ===
            migrationBuilder.Sql(@"
        INSERT INTO TagDrafts (Id, TagId, Name, Description, ReviewStatus, SubmittedAt)
        SELECT NEWID(), Id, Name, Description, 1, SYSUTCDATETIME()
        FROM Tags
        WHERE ReviewStatus = 1
    ");

            // === ✅ 删除 Tags 中的 ReviewStatus 字段 ===
            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "Tags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeNodeDrafts");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "TagDrafts");
        }
    }
}
