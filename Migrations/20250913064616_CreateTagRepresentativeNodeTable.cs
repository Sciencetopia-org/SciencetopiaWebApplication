using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class CreateTagRepresentativeNodeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagRepresentativeNode",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagRepresentativeNode", x => new { x.TagId, x.NodeId });
                    table.ForeignKey(
                        name: "FK_TagRepresentativeNode_KnowledgeNodes_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagRepresentativeNode_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagRepresentativeNode_NodeId",
                schema: "KnowledgeGraph",
                table: "TagRepresentativeNode",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagRepresentativeNode",
                schema: "KnowledgeGraph");
        }
    }
}
