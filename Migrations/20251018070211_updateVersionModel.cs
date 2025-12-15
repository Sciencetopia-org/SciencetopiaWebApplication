using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class updateVersionModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Align filtered unique indexes to IsCurrent=1 per spec and tighten latest views
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_KN_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[KnowledgeNodes]'))
    DROP INDEX [UX_KN_Stable_Current] ON [KnowledgeGraph].[KnowledgeNodes];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Tags_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[Tags]'))
    DROP INDEX [UX_Tags_Stable_Current] ON [KnowledgeGraph].[Tags];

CREATE UNIQUE INDEX UX_KN_Stable_Current ON KnowledgeGraph.KnowledgeNodes(StableId) WHERE IsCurrent = 1;
CREATE INDEX IX_KN_Stable_Version ON KnowledgeGraph.KnowledgeNodes(StableId, VersionNumber DESC);
CREATE UNIQUE INDEX UX_Tags_Stable_Current ON KnowledgeGraph.Tags(StableId) WHERE IsCurrent = 1;
CREATE INDEX IX_Tags_Stable_Version ON KnowledgeGraph.Tags(StableId, VersionNumber DESC);

EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestKnowledgeNodes AS
SELECT * FROM KnowledgeGraph.KnowledgeNodes WHERE Status = ''Current'' AND IsCurrent = 1');
EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestTags AS
SELECT * FROM KnowledgeGraph.Tags WHERE Status = ''Current'' AND IsCurrent = 1');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestKnowledgeNodes AS
SELECT * FROM KnowledgeGraph.KnowledgeNodes WHERE Status = ''Current''');
EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestTags AS
SELECT * FROM KnowledgeGraph.Tags WHERE Status = ''Current''');

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_KN_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[KnowledgeNodes]'))
    DROP INDEX [UX_KN_Stable_Current] ON [KnowledgeGraph].[KnowledgeNodes];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Tags_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[Tags]'))
    DROP INDEX [UX_Tags_Stable_Current] ON [KnowledgeGraph].[Tags];

CREATE UNIQUE INDEX UX_KN_Stable_Current ON KnowledgeGraph.KnowledgeNodes(StableId) WHERE Status = 'Current';
CREATE INDEX IX_KN_Stable_Version ON KnowledgeGraph.KnowledgeNodes(StableId, VersionNumber DESC);
CREATE UNIQUE INDEX UX_Tags_Stable_Current ON KnowledgeGraph.Tags(StableId) WHERE Status = 'Current';
CREATE INDEX IX_Tags_Stable_Version ON KnowledgeGraph.Tags(StableId, VersionNumber DESC);
            ");
        }
    }
}
