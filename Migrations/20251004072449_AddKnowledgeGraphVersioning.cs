using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeGraphVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 0) 防御式删除可能已存在的索引（避免重复运行、或上次失败留下的残余）
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_KN_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[KnowledgeNodes]'))
    DROP INDEX [UX_KN_Stable_Current] ON [KnowledgeGraph].[KnowledgeNodes];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name='UX_Tags_Stable_Current' AND object_id=OBJECT_ID(N'[KnowledgeGraph].[Tags]'))
    DROP INDEX [UX_Tags_Stable_Current] ON [KnowledgeGraph].[Tags];
");

            // 1) 清理旧字段（按你原本所需）
            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            // 2) 新增列（可空）——保持与你原始代码一致
            migrationBuilder.AddColumn<Guid>(
                name: "StableId",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StableId",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedBy",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            // 3) RowVersion
            migrationBuilder.Sql("IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes','RowVersion') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD RowVersion ROWVERSION;");
            migrationBuilder.Sql("IF COL_LENGTH('KnowledgeGraph.Tags','RowVersion') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD RowVersion ROWVERSION;");

            // 4) 时间列类型调和（守护式，列存在才 ALTER；不存在就保持后面 ADD 阶段）
            migrationBuilder.Sql(@"
IF COL_LENGTH('KnowledgeGraph.Tags', 'RetiredAt') IS NOT NULL
BEGIN
    DECLARE @c sysname;
    SELECT @c = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.Tags') AND c.name = 'RetiredAt';
    IF @c IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.Tags DROP CONSTRAINT [' + @c + ']');
    ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN RetiredAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.Tags', 'PublishedAt') IS NOT NULL
BEGIN
    DECLARE @c2 sysname;
    SELECT @c2 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.Tags') AND c.name = 'PublishedAt';
    IF @c2 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.Tags DROP CONSTRAINT [' + @c2 + ']');
    ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN PublishedAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.Tags', 'CreatedAt') IS NOT NULL
BEGIN
    DECLARE @c3 sysname;
    SELECT @c3 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.Tags') AND c.name = 'CreatedAt';
    IF @c3 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.Tags DROP CONSTRAINT [' + @c3 + ']');
    ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN CreatedAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.Tags', 'ApprovedAt') IS NOT NULL
BEGIN
    DECLARE @c4 sysname;
    SELECT @c4 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.Tags') AND c.name = 'ApprovedAt';
    IF @c4 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.Tags DROP CONSTRAINT [' + @c4 + ']');
    ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN ApprovedAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'RetiredAt') IS NOT NULL
BEGIN
    DECLARE @kc sysname;
    SELECT @kc = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.KnowledgeNodes') AND c.name = 'RetiredAt';
    IF @kc IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP CONSTRAINT [' + @kc + ']');
    ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN RetiredAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'PublishedAt') IS NOT NULL
BEGIN
    DECLARE @kc2 sysname;
    SELECT @kc2 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.KnowledgeNodes') AND c.name = 'PublishedAt';
    IF @kc2 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP CONSTRAINT [' + @kc2 + ']');
    ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN PublishedAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'CreatedAt') IS NOT NULL
BEGIN
    DECLARE @kc3 sysname;
    SELECT @kc3 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.KnowledgeNodes') AND c.name = 'CreatedAt';
    IF @kc3 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP CONSTRAINT [' + @kc3 + ']');
    ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN CreatedAt datetimeoffset NULL;
END;

IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'ApprovedAt') IS NOT NULL
BEGIN
    DECLARE @kc4 sysname;
    SELECT @kc4 = d.name
    FROM sys.default_constraints d
    JOIN sys.columns c ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id
    WHERE d.parent_object_id = OBJECT_ID('KnowledgeGraph.KnowledgeNodes') AND c.name = 'ApprovedAt';
    IF @kc4 IS NOT NULL EXEC('ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP CONSTRAINT [' + @kc4 + ']');
    ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN ApprovedAt datetimeoffset NULL;
END;
");

            // 5) 守护式 ADD（若 4 中不存在就新增）
            migrationBuilder.Sql(@"
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'StableId') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD StableId uniqueidentifier NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'VersionNumber') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD VersionNumber int NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'Status') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD Status nvarchar(32) NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'IsCurrent') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD IsCurrent bit NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'CreatedAt') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD CreatedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'CreatedBy') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD CreatedBy nvarchar(128) NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'PublishedAt') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD PublishedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'ApprovedAt') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD ApprovedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.KnowledgeNodes', 'ApprovedBy') IS NULL ALTER TABLE KnowledgeGraph.KnowledgeNodes ADD ApprovedBy nvarchar(128) NULL;

IF COL_LENGTH('KnowledgeGraph.Tags', 'StableId') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD StableId uniqueidentifier NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'VersionNumber') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD VersionNumber int NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'Status') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD Status nvarchar(32) NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'IsCurrent') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD IsCurrent bit NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'CreatedAt') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD CreatedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'CreatedBy') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD CreatedBy nvarchar(128) NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'PublishedAt') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD PublishedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'ApprovedAt') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD ApprovedAt datetimeoffset NULL;
IF COL_LENGTH('KnowledgeGraph.Tags', 'ApprovedBy') IS NULL ALTER TABLE KnowledgeGraph.Tags ADD ApprovedBy nvarchar(128) NULL;
");

            // 6) 回填与默认值（把空值补齐）
            migrationBuilder.Sql(@"
UPDATE KnowledgeGraph.KnowledgeNodes
SET StableId      = ISNULL(StableId, NEWID()),
    VersionNumber = ISNULL(VersionNumber, 1),
    Status        = ISNULL(Status, 'Current'),
    IsCurrent     = ISNULL(IsCurrent, 1),
    PublishedAt   = ISNULL(PublishedAt, SYSDATETIMEOFFSET()),
    CreatedAt     = ISNULL(CreatedAt, PublishedAt)
WHERE StableId IS NULL
   OR VersionNumber IS NULL
   OR Status IS NULL
   OR IsCurrent IS NULL
   OR PublishedAt IS NULL
   OR CreatedAt IS NULL;

UPDATE KnowledgeGraph.Tags
SET StableId      = ISNULL(StableId, NEWID()),
    VersionNumber = ISNULL(VersionNumber, 1),
    Status        = ISNULL(Status, 'Current'),
    IsCurrent     = ISNULL(IsCurrent, 1),
    PublishedAt   = ISNULL(PublishedAt, SYSDATETIMEOFFSET()),
    CreatedAt     = ISNULL(CreatedAt, PublishedAt)
WHERE StableId IS NULL
   OR VersionNumber IS NULL
   OR Status IS NULL
   OR IsCurrent IS NULL
   OR PublishedAt IS NULL
   OR CreatedAt IS NULL;

UPDATE KnowledgeGraph.KnowledgeNodes SET ApprovedAt = ISNULL(ApprovedAt, PublishedAt);
UPDATE KnowledgeGraph.Tags          SET ApprovedAt = ISNULL(ApprovedAt, PublishedAt);

UPDATE KnowledgeGraph.KnowledgeNodes SET ApprovedBy = ISNULL(ApprovedBy, CreatedBy);
UPDATE KnowledgeGraph.Tags          SET ApprovedBy = ISNULL(ApprovedBy, CreatedBy);

UPDATE KnowledgeGraph.KnowledgeNodes SET CreatedBy = ISNULL(CreatedBy, 'system');
UPDATE KnowledgeGraph.Tags          SET CreatedBy = ISNULL(CreatedBy, 'system');
UPDATE KnowledgeGraph.KnowledgeNodes SET ApprovedBy = ISNULL(ApprovedBy, 'system');
UPDATE KnowledgeGraph.Tags          SET ApprovedBy = ISNULL(ApprovedBy, 'system');

UPDATE KnowledgeGraph.KnowledgeNodes SET IsCurrent = CASE WHEN Status = 'Current' THEN 1 ELSE 0 END;
UPDATE KnowledgeGraph.Tags          SET IsCurrent = CASE WHEN Status = 'Current' THEN 1 ELSE 0 END;

UPDATE KnowledgeGraph.KnowledgeNodes SET VersionNumber = 1 WHERE VersionNumber IS NULL;
UPDATE KnowledgeGraph.Tags          SET VersionNumber = 1 WHERE VersionNumber IS NULL;
");

            // 7) 把需要的列改为 NOT NULL（此时没有索引依赖，安全）
            //   用 SQL 防御式改型，然后 EF 的 AlterColumn 也可保留；但避免再带 defaultValue 让 EF帮你加默认约束。
            migrationBuilder.Sql(@"
-- KnowledgeNodes
ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN StableId uniqueidentifier NOT NULL;
ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN VersionNumber int NOT NULL;
ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN Status nvarchar(32) NOT NULL;
ALTER TABLE KnowledgeGraph.KnowledgeNodes ALTER COLUMN IsCurrent bit NOT NULL;

-- Tags
ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN StableId uniqueidentifier NOT NULL;
ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN VersionNumber int NOT NULL;
ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN Status nvarchar(32) NOT NULL;
ALTER TABLE KnowledgeGraph.Tags ALTER COLUMN IsCurrent bit NOT NULL;
");

            // 8) 现在再建索引（改成基于 Status 的过滤唯一索引，避免耦合 IsCurrent）
            migrationBuilder.Sql(@"
CREATE UNIQUE INDEX UX_KN_Stable_Current ON KnowledgeGraph.KnowledgeNodes(StableId) WHERE Status = 'Current';
CREATE INDEX IX_KN_Stable_Version ON KnowledgeGraph.KnowledgeNodes(StableId, VersionNumber DESC);

CREATE UNIQUE INDEX UX_Tags_Stable_Current ON KnowledgeGraph.Tags(StableId) WHERE Status = 'Current';
CREATE INDEX IX_Tags_Stable_Version ON KnowledgeGraph.Tags(StableId, VersionNumber DESC);
");

            // 9) 视图（建议也只基于 Status='Current'；你可以保留 IsCurrent 判断，但没必要）
            migrationBuilder.Sql("EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestKnowledgeNodes AS SELECT * FROM KnowledgeGraph.KnowledgeNodes WHERE Status = ''Current''');");
            migrationBuilder.Sql("EXEC('CREATE OR ALTER VIEW KnowledgeGraph.vLatestTags AS SELECT * FROM KnowledgeGraph.Tags WHERE Status = ''Current''');");

            // 10) 移除 EF 生成的 AlterColumn 默认段，避免再次尝试在有索引时改列（你上面的 AlterColumn 块请删除）
            // 即：删除你原文末尾那一大段 migrationBuilder.AlterColumn<...> 调用（StableId/VersionNumber/Status/IsCurrent）。
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS KnowledgeGraph.vLatestKnowledgeNodes;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS KnowledgeGraph.vLatestTags;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS UX_KN_Stable_Current ON KnowledgeGraph.KnowledgeNodes;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_KN_Stable_Version ON KnowledgeGraph.KnowledgeNodes;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS UX_Tags_Stable_Current ON KnowledgeGraph.Tags;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_Tags_Stable_Version ON KnowledgeGraph.Tags;");

            migrationBuilder.Sql("ALTER TABLE KnowledgeGraph.KnowledgeNodes DROP COLUMN RowVersion;");
            migrationBuilder.Sql("ALTER TABLE KnowledgeGraph.Tags DROP COLUMN RowVersion;");

            migrationBuilder.AlterColumn<DateTime>(
                name: "RetiredAt",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PublishedAt",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ApprovedAt",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "RetiredAt",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PublishedAt",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ApprovedAt",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "StableId",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "StableId",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "ApprovedBy",
                schema: "KnowledgeGraph",
                table: "Tags");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedDate",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedDate",
                schema: "KnowledgeGraph",
                table: "Tags",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedDate",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedDate",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodes",
                type: "datetimeoffset",
                nullable: true);
        }
    }
}
