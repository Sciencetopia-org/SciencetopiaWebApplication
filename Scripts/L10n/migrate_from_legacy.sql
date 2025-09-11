-- One-time data migration: KnowledgeNodeTranslations -> L10nSets/L10nItems (fieldKey='title')
-- Dry-run support: set @dry_run = 1 to only report counts

DECLARE @dry_run bit = 1;

IF OBJECT_ID('tempdb..#NodeSetMap') IS NOT NULL DROP TABLE #NodeSetMap;
CREATE TABLE #NodeSetMap (NodeId uniqueidentifier PRIMARY KEY, L10nSetId uniqueidentifier NOT NULL);

-- 1) Create a set for each KnowledgeNode
INSERT INTO #NodeSetMap(NodeId, L10nSetId)
SELECT n.Id, NEWID()
FROM dbo.KnowledgeNodes n WITH (NOLOCK)
WHERE n.Id IS NOT NULL;

IF (@dry_run = 0)
BEGIN
  INSERT INTO dbo.L10nSets(L10nSetId, Scope, IsManaged)
  SELECT m.L10nSetId, 'knowledge_node', 0 FROM #NodeSetMap m;

  -- 2) NodeL10nSets + DefaultL10nSetId
  INSERT INTO dbo.NodeL10nSets(NodeId, L10nSetId, Relation)
  SELECT m.NodeId, m.L10nSetId, 0 FROM #NodeSetMap m;

  UPDATE kn
    SET kn.DefaultL10nSetId = m.L10nSetId
  FROM dbo.KnowledgeNodes kn
  JOIN #NodeSetMap m ON m.NodeId = kn.Id;
END

-- 3) Insert L10nItems for translations
;WITH T AS (
  SELECT t.NodeId, t.Language AS Lang, t.Name AS Title
  FROM dbo.KnowledgeNodeTranslations t WITH (NOLOCK)
)
SELECT COUNT(*) AS TranslationsCount FROM T;

IF (@dry_run = 0)
BEGIN
  INSERT INTO dbo.L10nItems(L10nItemId, FieldKey, LangCode, Kind, Text)
  SELECT NEWID(), 'title', T.Lang, 0, T.Title
  FROM T WHERE T.Title IS NOT NULL;

  -- 4) Link items to sets
  INSERT INTO dbo.L10nSetItems(L10nSetId, L10nItemId)
  SELECT m.L10nSetId, i.L10nItemId
  FROM dbo.L10nItems i
  JOIN T ON T.Title = i.Text AND i.FieldKey = 'title' AND i.LangCode = T.Lang
  JOIN #NodeSetMap m ON m.NodeId = T.NodeId;
END

-- 4b) Also create a null-lang primary title from base Name when present
;WITH B AS (
  SELECT n.Id AS NodeId, n.Name AS Title FROM dbo.KnowledgeNodes n WITH (NOLOCK) WHERE n.Name IS NOT NULL
)
SELECT COUNT(*) AS BaseNameCount FROM B;

IF (@dry_run = 0)
BEGIN
  INSERT INTO dbo.L10nItems(L10nItemId, FieldKey, LangCode, Kind, Text)
  SELECT NEWID(), 'title', NULL, 0, B.Title FROM B;

  INSERT INTO dbo.L10nSetItems(L10nSetId, L10nItemId)
  SELECT m.L10nSetId, i.L10nItemId
  FROM dbo.L10nItems i
  JOIN B ON B.Title = i.Text AND i.FieldKey = 'title' AND i.LangCode IS NULL
  JOIN #NodeSetMap m ON m.NodeId = B.NodeId;
END

-- Report
SELECT (SELECT COUNT(*) FROM #NodeSetMap) AS Nodes,
       (SELECT COUNT(*) FROM dbo.L10nSets) AS L10nSets,
       (SELECT COUNT(*) FROM dbo.L10nItems) AS L10nItems;

