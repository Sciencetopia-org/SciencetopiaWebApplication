using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class UseTagStableIdForTagTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DECLARE @fkName NVARCHAR(128);
SELECT @fkName = fk.name
FROM sys.foreign_keys fk
JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
JOIN sys.tables rt ON fk.referenced_object_id = rt.object_id
WHERE pt.schema_id = SCHEMA_ID('KnowledgeGraph') AND pt.name = 'TagTypes'
  AND rt.schema_id = SCHEMA_ID('KnowledgeGraph') AND rt.name = 'Tags';
IF @fkName IS NOT NULL
    EXEC('ALTER TABLE [KnowledgeGraph].[TagTypes] DROP CONSTRAINT [' + @fkName + ']');
");

            migrationBuilder.RenameColumn(
                name: "TagId",
                schema: "KnowledgeGraph",
                table: "TagTypes",
                newName: "TagStableId");

            migrationBuilder.Sql(
                @"UPDATE tt
                  SET TagStableId = t.StableId
                  FROM KnowledgeGraph.TagTypes tt
                  INNER JOIN KnowledgeGraph.Tags t ON t.Id = tt.TagStableId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"UPDATE tt
                  SET TagStableId = t.Id
                  FROM KnowledgeGraph.TagTypes tt
                  INNER JOIN KnowledgeGraph.Tags t ON t.StableId = tt.TagStableId AND t.IsCurrent = 1"
            );

            migrationBuilder.RenameColumn(
                name: "TagStableId",
                schema: "KnowledgeGraph",
                table: "TagTypes",
                newName: "TagId");

            migrationBuilder.Sql(@"
DECLARE @fkName NVARCHAR(128);
SELECT @fkName = fk.name
FROM sys.foreign_keys fk
JOIN sys.tables pt ON fk.parent_object_id = pt.object_id
WHERE pt.schema_id = SCHEMA_ID('KnowledgeGraph') AND pt.name = 'TagTypes'
  AND fk.referenced_object_id = OBJECT_ID('[KnowledgeGraph].[Tags]');
IF @fkName IS NULL
    ALTER TABLE [KnowledgeGraph].[TagTypes]
    ADD CONSTRAINT [FK_TagTypes_Tags_TagId] FOREIGN KEY ([TagId])
        REFERENCES [KnowledgeGraph].[Tags]([Id]);
");
        }
    }
}
