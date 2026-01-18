using Microsoft.EntityFrameworkCore.Migrations;

namespace SciencetopiaWebApplication.Migrations
{
    public partial class AddTagL10nDomain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('dbo.Tags','DefaultL10nSetId') IS NULL
BEGIN
  ALTER TABLE dbo.Tags ADD DefaultL10nSetId UNIQUEIDENTIFIER NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Tag_DefaultL10nSet')
BEGIN
  ALTER TABLE dbo.Tags
    ADD CONSTRAINT FK_Tag_DefaultL10nSet FOREIGN KEY (DefaultL10nSetId) REFERENCES dbo.L10nSets(L10nSetId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Tag_DefaultL10nSet' AND object_id = OBJECT_ID('dbo.Tags'))
BEGIN
  CREATE UNIQUE INDEX UX_Tag_DefaultL10nSet
    ON dbo.Tags(DefaultL10nSetId) WHERE DefaultL10nSetId IS NOT NULL;
END

IF OBJECT_ID('dbo.TagL10nSets','U') IS NULL
BEGIN
  CREATE TABLE dbo.TagL10nSets (
    TagId UNIQUEIDENTIFIER NOT NULL,
    L10nSetId UNIQUEIDENTIFIER NOT NULL,
    Relation TINYINT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_TagL10nSets PRIMARY KEY (TagId, L10nSetId, Relation),
    CONSTRAINT FK_TagL10nSets_Tag FOREIGN KEY (TagId) REFERENCES dbo.Tags(Id) ON DELETE CASCADE,
    CONSTRAINT FK_TagL10nSets_Set FOREIGN KEY (L10nSetId) REFERENCES dbo.L10nSets(L10nSetId) ON DELETE CASCADE
  );
  CREATE INDEX IX_TagL10nSets_Tag ON dbo.TagL10nSets(TagId);
  CREATE INDEX IX_TagL10nSets_Set ON dbo.TagL10nSets(L10nSetId);
END

");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.TagL10nSets','U') IS NOT NULL DROP TABLE dbo.TagL10nSets;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Tag_DefaultL10nSet')
  ALTER TABLE dbo.Tags DROP CONSTRAINT FK_Tag_DefaultL10nSet;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Tag_DefaultL10nSet' AND object_id = OBJECT_ID('dbo.Tags'))
  DROP INDEX UX_Tag_DefaultL10nSet ON dbo.Tags;
IF COL_LENGTH('dbo.Tags','DefaultL10nSetId') IS NOT NULL
  ALTER TABLE dbo.Tags DROP COLUMN DefaultL10nSetId;
");
        }
    }
}

