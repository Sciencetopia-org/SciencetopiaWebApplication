-- Drop legacy translation tables (after data migration to L10n)
IF OBJECT_ID('dbo.KnowledgeNodeTranslations','U') IS NOT NULL
    DROP TABLE dbo.KnowledgeNodeTranslations;

IF OBJECT_ID('dbo.TagTranslations','U') IS NOT NULL
    DROP TABLE dbo.TagTranslations;

