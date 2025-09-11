using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddL10n : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultL10nSetId",
                table: "Tags",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DefaultL10nSetId",
                table: "KnowledgeNodes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "L10nItems",
                columns: table => new
                {
                    L10nItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, defaultValue: "title"),
                    LangCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    ScriptCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_L10nItems", x => x.L10nItemId);
                });

            migrationBuilder.CreateTable(
                name: "L10nSets",
                columns: table => new
                {
                    L10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PolicyJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsManaged = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_L10nSets", x => x.L10nSetId);
                });

            migrationBuilder.CreateTable(
                name: "L10nSetItems",
                columns: table => new
                {
                    L10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    L10nItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_L10nSetItems", x => new { x.L10nSetId, x.L10nItemId });
                    table.ForeignKey(
                        name: "FK_L10nSetItems_L10nItems_L10nItemId",
                        column: x => x.L10nItemId,
                        principalTable: "L10nItems",
                        principalColumn: "L10nItemId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_L10nSetItems_L10nSets_L10nSetId",
                        column: x => x.L10nSetId,
                        principalTable: "L10nSets",
                        principalColumn: "L10nSetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeL10nSets",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    L10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Relation = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeL10nSets", x => new { x.NodeId, x.L10nSetId, x.Relation });
                    table.ForeignKey(
                        name: "FK_NodeL10nSets_KnowledgeNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NodeL10nSets_L10nSets_L10nSetId",
                        column: x => x.L10nSetId,
                        principalTable: "L10nSets",
                        principalColumn: "L10nSetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagL10nSets",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    L10nSetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Relation = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)0),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagL10nSets", x => new { x.TagId, x.L10nSetId, x.Relation });
                    table.ForeignKey(
                        name: "FK_TagL10nSets_L10nSets_L10nSetId",
                        column: x => x.L10nSetId,
                        principalTable: "L10nSets",
                        principalColumn: "L10nSetId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagL10nSets_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Tag_DefaultL10nSet",
                table: "Tags",
                column: "DefaultL10nSetId",
                unique: true,
                filter: "[DefaultL10nSetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_KN_DefaultL10nSet",
                table: "KnowledgeNodes",
                column: "DefaultL10nSetId",
                unique: true,
                filter: "[DefaultL10nSetId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_L10nItems_Field_Lang",
                table: "L10nItems",
                columns: new[] { "FieldKey", "LangCode" });

            migrationBuilder.CreateIndex(
                name: "IX_L10nItems_Text",
                table: "L10nItems",
                column: "Text");

            migrationBuilder.CreateIndex(
                name: "IX_L10nSetItems_Item",
                table: "L10nSetItems",
                column: "L10nItemId");

            migrationBuilder.CreateIndex(
                name: "IX_L10nSets_Scope",
                table: "L10nSets",
                column: "Scope");

            migrationBuilder.CreateIndex(
                name: "IX_NodeL10nSets_Node",
                table: "NodeL10nSets",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeL10nSets_Set",
                table: "NodeL10nSets",
                column: "L10nSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TagL10nSets_Set",
                table: "TagL10nSets",
                column: "L10nSetId");

            migrationBuilder.CreateIndex(
                name: "IX_TagL10nSets_Tag",
                table: "TagL10nSets",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeNodes_L10nSets_DefaultL10nSetId",
                table: "KnowledgeNodes",
                column: "DefaultL10nSetId",
                principalTable: "L10nSets",
                principalColumn: "L10nSetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_L10nSets_DefaultL10nSetId",
                table: "Tags",
                column: "DefaultL10nSetId",
                principalTable: "L10nSets",
                principalColumn: "L10nSetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeNodes_L10nSets_DefaultL10nSetId",
                table: "KnowledgeNodes");

            migrationBuilder.DropForeignKey(
                name: "FK_Tags_L10nSets_DefaultL10nSetId",
                table: "Tags");

            migrationBuilder.DropTable(
                name: "L10nSetItems");

            migrationBuilder.DropTable(
                name: "NodeL10nSets");

            migrationBuilder.DropTable(
                name: "TagL10nSets");

            migrationBuilder.DropTable(
                name: "L10nItems");

            migrationBuilder.DropTable(
                name: "L10nSets");

            migrationBuilder.DropIndex(
                name: "UX_Tag_DefaultL10nSet",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "UX_KN_DefaultL10nSet",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "DefaultL10nSetId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "DefaultL10nSetId",
                table: "KnowledgeNodes");
        }
    }
}
