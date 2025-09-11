# L10n Domain (Sets/Items)

This document summarizes the schema, migration, feature flag, and APIs introduced for generalized localization.

## Feature Flag

- Key: `L10n.Enabled` (bool, default false) in `appsettings.json`.
- When enabled, node display name/description reads go through `IL10nService`.
- When disabled, legacy `KnowledgeNodeTranslations` are used.

## Schema

Tables:
- `L10nSets(L10nSetId, Scope, PolicyJson, IsManaged, CreatedAt, UpdatedAt, RowVersion)`
- `L10nItems(L10nItemId, FieldKey, LangCode, ScriptCode, Kind, Text, Content, SortOrder, CreatedAt, UpdatedAt, RowVersion)`
- `KnowledgeNodes(DefaultL10nSetId NULL FK -> L10nSets)` + filtered unique index
- `NodeL10nSets(NodeId, L10nSetId, Relation, CreatedAt)`
- `Tags(DefaultL10nSetId NULL FK -> L10nSets)` + filtered unique index
- `TagL10nSets(TagId, L10nSetId, Relation, CreatedAt)`
- `L10nSetItems(L10nSetId, L10nItemId, CreatedAt)`

EF: see `Models/L10n/L10nModels.cs`, mappings in `Data/ApplicationDbContext.cs`.

## Migration

- EF migrations:
  - `Migrations/20250910000000_AddL10nDomain.cs` (core L10n tables + node link)
  - `Migrations/20250910001000_AddTagL10nDomain.cs` (tag link)
- SQL scripts:
  - `Scripts/L10n/ddl.sql` – creates five tables + FK/index
  - `Scripts/L10n/migrate_from_legacy.sql` – one-time migration; supports dry-run

## One-time Data Migration

- Runner: `Services/L10n/L10nMigrationRunner.cs` with `MigrateFromLegacyAsync(dryRun)`
- Admin API: `POST /api/admin/l10n/migrate?dryRun=true|false`
- Logic:
  1) Create `L10nSet` for each `KnowledgeNode`
  2) Link with `NodeL10nSets` and set `DefaultL10nSetId`
  3) For each `KnowledgeNodeTranslations` row, create items for `title` and `description`
  4) Link items via `L10nSetItems`
  5) For base `KnowledgeNodes.Name/Description`, detect language (`zh`/`en`) and insert with detected `LangCode`
  6) Repeat 1–5 for Tags (`TagTranslations`, base `Tags.Name/Description`, Scope `tag`)

## Service & Caching

- Interface: `Services/L10n/IL10nService`
- Cache key: `l10n:node:{nodeId}:field:{fieldKey}:lang:{lang}`
- Selection: exact `LangCode` + `Kind=Primary` first; fallback to `LangCode IS NULL`

## API (minimal)

- `GET /api/nodes/{id}/l10n?field=title&lang=zh-TW` – returns string?
- `GET /api/nodes/{id}/l10n/items?field=description` – returns items list
- `POST /api/nodes/{id}/l10n` – upsert one item (admin)
- `DELETE /api/nodes/{id}/l10n/{itemId}` – remove item (admin)
 - `GET /api/tags/{id}/l10n?field=title&lang=zh-TW` – returns string?
 - `GET /api/tags/{id}/l10n/items?field=description` – returns items list
 - `POST /api/tags/{id}/l10n` – upsert one item (admin)
 - `DELETE /api/tags/{id}/l10n/{itemId}` – remove item (admin)

## Gradual Rollout & Rollback

- Toggle `L10n.Enabled` to enable/disable new reads.
- Legacy read path remains in repositories; service overrides values only when flag is on.
- Rollback: set flag to false; no data rollback needed.

## Checklist

- [ ] EF migration applied; five tables present; FK/indices valid
- [ ] Migration runner executed; each node has a set; title coverage ≥ 99.9%
- [ ] API returns names consistent with legacy ≥ 99.5%
- [ ] Cache invalidation works on upsert/delete
- [ ] Toggle works with no errors
