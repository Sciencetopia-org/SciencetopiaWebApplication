using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Sciencetopia.Data;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20251019000000_AddRetiredAtToKnowledgeGraph")]
    public partial class AddRetiredAtToKnowledgeGraph : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add RetiredAt to KnowledgeGraph tables if missing (idempotent)
            migrationBuilder.Sql(@"
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'RetiredAt') IS NULL 
    ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD RetiredAt datetimeoffset NULL;

IF COL_LENGTH('KnowledgeGraph.Tags', 'RetiredAt') IS NULL 
    ALTER TABLE KnowledgeGraph.Tags ADD RetiredAt datetimeoffset NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'RetiredAt') IS NOT NULL 
    ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP COLUMN RetiredAt;

IF COL_LENGTH('KnowledgeGraph.Tags', 'RetiredAt') IS NOT NULL 
    ALTER TABLE KnowledgeGraph.Tags DROP COLUMN RetiredAt;
            ");
        }
    }
}
