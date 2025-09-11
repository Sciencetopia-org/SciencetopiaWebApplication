using Microsoft.EntityFrameworkCore.Migrations;

namespace SciencetopiaWebApplication.Migrations
{
    public partial class DropLegacyTranslations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.KnowledgeNodeTranslations','U') IS NOT NULL DROP TABLE dbo.KnowledgeNodeTranslations;
IF OBJECT_ID('dbo.TagTranslations','U') IS NOT NULL DROP TABLE dbo.TagTranslations;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: legacy tables intentionally removed
        }
    }
}

