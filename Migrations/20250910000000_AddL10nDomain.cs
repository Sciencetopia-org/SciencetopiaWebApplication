using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SciencetopiaWebApplication.Migrations
{
    public partial class AddL10nDomain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.L10nSets','U') IS NULL
BEGIN
  CREATE TABLE dbo.L10nSets (
    L10nSetId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    Scope NVARCHAR(50) NOT NULL,
    PolicyJson NVARCHAR(MAX) NULL,
    IsManaged BIT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION
  );
  CREATE INDEX IX_L10nSets_Scope ON dbo.L10nSets(Scope);
END

IF OBJECT_ID('dbo.L10nItems','U') IS NULL
BEGIN
  CREATE TABLE dbo.L10nItems (
    L10nItemId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    FieldKey NVARCHAR(32) NOT NULL DEFAULT 'title',
    LangCode NVARCHAR(10) NULL,
    ScriptCode NVARCHAR(10) NULL,
    Kind TINYINT NOT NULL DEFAULT 0,
    Text NVARCHAR(200) NULL,
    Content NVARCHAR(MAX) NULL,
    SortOrder INT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    RowVersion ROWVERSION
  );
  CREATE INDEX IX_L10nItems_Field_Lang ON dbo.L10nItems(FieldKey, LangCode);
  CREATE INDEX IX_L10nItems_Text       ON dbo.L10nItems(Text);
END

IF COL_LENGTH('dbo.KnowledgeNodes','DefaultL10nSetId') IS NULL
BEGIN
  ALTER TABLE dbo.KnowledgeNodes ADD DefaultL10nSetId UNIQUEIDENTIFIER NULL;
END

IF NOT EXISTS (
  SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_KN_DefaultL10nSet')
BEGIN
  ALTER TABLE dbo.KnowledgeNodes
    ADD CONSTRAINT FK_KN_DefaultL10nSet FOREIGN KEY (DefaultL10nSetId) REFERENCES dbo.L10nSets(L10nSetId);
END

IF NOT EXISTS (
  SELECT 1 FROM sys.indexes WHERE name = 'UX_KN_DefaultL10nSet' AND object_id = OBJECT_ID('dbo.KnowledgeNodes'))
BEGIN
  CREATE UNIQUE INDEX UX_KN_DefaultL10nSet
    ON dbo.KnowledgeNodes(DefaultL10nSetId) WHERE DefaultL10nSetId IS NOT NULL;
END

IF OBJECT_ID('dbo.NodeL10nSets','U') IS NULL
BEGIN
  CREATE TABLE dbo.NodeL10nSets (
    NodeId UNIQUEIDENTIFIER NOT NULL,
    L10nSetId UNIQUEIDENTIFIER NOT NULL,
    Relation TINYINT NOT NULL DEFAULT 0,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_NodeL10nSets PRIMARY KEY (NodeId, L10nSetId, Relation),
    CONSTRAINT FK_NodeL10nSets_Node FOREIGN KEY (NodeId) REFERENCES dbo.KnowledgeNodes(Id) ON DELETE CASCADE,
    CONSTRAINT FK_NodeL10nSets_Set  FOREIGN KEY (L10nSetId) REFERENCES dbo.L10nSets(L10nSetId) ON DELETE CASCADE
  );
  CREATE INDEX IX_NodeL10nSets_Node ON dbo.NodeL10nSets(NodeId);
  CREATE INDEX IX_NodeL10nSets_Set  ON dbo.NodeL10nSets(L10nSetId);
END

IF OBJECT_ID('dbo.L10nSetItems','U') IS NULL
BEGIN
  CREATE TABLE dbo.L10nSetItems (
    L10nSetId UNIQUEIDENTIFIER NOT NULL,
    L10nItemId UNIQUEIDENTIFIER NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_L10nSetItems PRIMARY KEY (L10nSetId, L10nItemId),
    CONSTRAINT FK_L10nSetItems_Set  FOREIGN KEY (L10nSetId)  REFERENCES dbo.L10nSets(L10nSetId)   ON DELETE CASCADE,
    CONSTRAINT FK_L10nSetItems_Item FOREIGN KEY (L10nItemId) REFERENCES dbo.L10nItems(L10nItemId) ON DELETE CASCADE
  );
  CREATE INDEX IX_L10nSetItems_Item ON dbo.L10nSetItems(L10nItemId);
END

");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID('dbo.L10nSetItems','U') IS NOT NULL DROP TABLE dbo.L10nSetItems;
IF OBJECT_ID('dbo.NodeL10nSets','U') IS NOT NULL DROP TABLE dbo.NodeL10nSets;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_KN_DefaultL10nSet')
  ALTER TABLE dbo.KnowledgeNodes DROP CONSTRAINT FK_KN_DefaultL10nSet;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_KN_DefaultL10nSet' AND object_id = OBJECT_ID('dbo.KnowledgeNodes'))
  DROP INDEX UX_KN_DefaultL10nSet ON dbo.KnowledgeNodes;
IF COL_LENGTH('dbo.KnowledgeNodes','DefaultL10nSetId') IS NOT NULL
  ALTER TABLE dbo.KnowledgeNodes DROP COLUMN DefaultL10nSetId;
IF OBJECT_ID('dbo.L10nItems','U') IS NOT NULL DROP TABLE dbo.L10nItems;
IF OBJECT_ID('dbo.L10nSets','U') IS NOT NULL DROP TABLE dbo.L10nSets;
");
        }
    }
}

