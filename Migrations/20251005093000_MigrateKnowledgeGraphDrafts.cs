using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    public partial class MigrateKnowledgeGraphDrafts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE KnowledgeGraph.KnowledgeNodes
SET StableId = COALESCE(StableId, Id),
    VersionNumber = CASE WHEN VersionNumber IS NULL OR VersionNumber < 1 THEN 1 ELSE VersionNumber END,
    Status = COALESCE(NULLIF(Status, ''), 'Draft'),
    CreatedAt = COALESCE(CreatedAt, SYSUTCDATETIME()),
    CreatedBy = COALESCE(NULLIF(CreatedBy, ''), 'system'),
    ApprovedAt = CASE
        WHEN Status = 'Current' THEN COALESCE(ApprovedAt, PublishedAt, CreatedAt)
        ELSE ApprovedAt
    END,
    ApprovedBy = CASE
        WHEN Status = 'Current' THEN COALESCE(NULLIF(ApprovedBy, ''), NULLIF(CreatedBy, ''), 'system')
        ELSE ApprovedBy
    END,
    PublishedAt = CASE
        WHEN Status = 'Current' THEN COALESCE(PublishedAt, ApprovedAt, CreatedAt)
        ELSE PublishedAt
    END;

UPDATE KnowledgeGraph.Tags
SET StableId = COALESCE(StableId, Id),
    VersionNumber = CASE WHEN VersionNumber IS NULL OR VersionNumber < 1 THEN 1 ELSE VersionNumber END,
    Status = COALESCE(NULLIF(Status, ''), 'Draft'),
    CreatedAt = COALESCE(CreatedAt, SYSUTCDATETIME()),
    CreatedBy = COALESCE(NULLIF(CreatedBy, ''), 'system'),
    ApprovedAt = CASE
        WHEN Status = 'Current' THEN COALESCE(ApprovedAt, PublishedAt, CreatedAt)
        ELSE ApprovedAt
    END,
    ApprovedBy = CASE
        WHEN Status = 'Current' THEN COALESCE(NULLIF(ApprovedBy, ''), NULLIF(CreatedBy, ''), 'system')
        ELSE ApprovedBy
    END,
    PublishedAt = CASE
        WHEN Status = 'Current' THEN COALESCE(PublishedAt, ApprovedAt, CreatedAt)
        ELSE PublishedAt
    END;

UPDATE KnowledgeGraph.KnowledgeNodes
SET IsCurrent = CASE WHEN Status = 'Current' THEN 1 ELSE 0 END;

UPDATE KnowledgeGraph.Tags
SET IsCurrent = CASE WHEN Status = 'Current' THEN 1 ELSE 0 END;

UPDATE KnowledgeGraph.KnowledgeNodes
SET RetiredAt = CASE WHEN Status = 'Archived' AND RetiredAt IS NULL THEN ApprovedAt END;

UPDATE KnowledgeGraph.Tags
SET RetiredAt = CASE WHEN Status = 'Archived' AND RetiredAt IS NULL THEN ApprovedAt END;

UPDATE KnowledgeGraph.KnowledgeNodes
SET Status = 'Draft'
WHERE Status NOT IN ('Draft','Current','Archived','Rejected');

UPDATE KnowledgeGraph.Tags
SET Status = 'Draft'
WHERE Status NOT IN ('Draft','Current','Archived','Rejected');

UPDATE KnowledgeGraph.KnowledgeNodes
SET ApprovedBy = NULL
WHERE Status IN ('Draft','Rejected');

UPDATE KnowledgeGraph.KnowledgeNodes
SET ApprovedAt = NULL,
    PublishedAt = NULL
WHERE Status = 'Draft';

UPDATE KnowledgeGraph.Tags
SET ApprovedBy = NULL
WHERE Status IN ('Draft','Rejected');

UPDATE KnowledgeGraph.Tags
SET ApprovedAt = NULL,
    PublishedAt = NULL
WHERE Status = 'Draft';

");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'KnowledgeGraph' AND TABLE_NAME = 'KnowledgeNodeVersions')
BEGIN
    INSERT INTO KnowledgeGraph.KnowledgeNodes
        (Id, StableId, VersionNumber, Status, IsCurrent, PublishedAt, RetiredAt, CreatedAt, CreatedBy, ApprovedAt, ApprovedBy, Name, Description, DefaultL10nSetId)
    SELECT
        v.Id,
        COALESCE(n.StableId, v.NodeId),
        CASE WHEN v.VersionNumber IS NULL OR v.VersionNumber = 0 THEN 1 ELSE v.VersionNumber END,
        'Archived',
        0,
        COALESCE(v.PublishedAt, v.CreatedDate),
        COALESCE(v.PublishedAt, v.CreatedDate),
        v.CreatedDate,
        COALESCE(NULLIF(v.PublishedBy, ''), NULLIF(n.CreatedBy, ''), 'system'),
        COALESCE(v.PublishedAt, v.CreatedDate),
        COALESCE(NULLIF(v.PublishedBy, ''), NULLIF(n.CreatedBy, ''), 'system'),
        v.Name,
        v.Description,
        NULL
    FROM KnowledgeGraph.KnowledgeNodeVersions v
    LEFT JOIN KnowledgeGraph.KnowledgeNodes n ON n.Id = v.NodeId
    WHERE NOT EXISTS (
        SELECT 1
        FROM KnowledgeGraph.KnowledgeNodes existing
        WHERE existing.Id = v.Id
    );
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'KnowledgeGraph' AND TABLE_NAME = 'KnowledgeNodeDrafts')
BEGIN
    WITH DraftBases AS (
        SELECT
            d.Id,
            d.NodeId,
            d.Name,
            d.Description,
            d.ReviewStatus,
            d.SubmittedBy,
            d.SubmittedAt,
            d.ReviewedBy,
            d.ReviewedAt,
            COALESCE(kn.StableId, d.NodeId) AS StableId
        FROM KnowledgeGraph.KnowledgeNodeDrafts d
        LEFT JOIN KnowledgeGraph.KnowledgeNodes kn ON kn.Id = d.NodeId
    ),
    MaxVersions AS (
        SELECT StableId, MAX(VersionNumber) AS MaxVersion
        FROM KnowledgeGraph.KnowledgeNodes
        GROUP BY StableId
    ),
    Drafts AS (
        SELECT
            b.*, 
            COALESCE(m.MaxVersion, 0) AS BaseVersion,
            ROW_NUMBER() OVER (PARTITION BY b.StableId ORDER BY b.SubmittedAt, b.Id) AS DraftOrder
        FROM DraftBases b
        LEFT JOIN MaxVersions m ON m.StableId = b.StableId
    )
    INSERT INTO KnowledgeGraph.KnowledgeNodes
        (Id, StableId, VersionNumber, Status, IsCurrent, CreatedAt, CreatedBy, PublishedAt, ApprovedAt, ApprovedBy, RetiredAt, Name, Description, DefaultL10nSetId)
    SELECT
        d.Id,
        d.StableId,
        d.BaseVersion + d.DraftOrder,
        CASE d.ReviewStatus WHEN 0 THEN 'Archived' WHEN 2 THEN 'Rejected' ELSE 'Draft' END,
        0,
        d.SubmittedAt,
        NULLIF(d.SubmittedBy, ''),
        CASE d.ReviewStatus WHEN 0 THEN COALESCE(d.ReviewedAt, d.SubmittedAt) ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN d.ReviewedAt WHEN 2 THEN d.ReviewedAt ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN NULLIF(d.ReviewedBy, '') WHEN 2 THEN NULLIF(d.ReviewedBy, '') ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN d.ReviewedAt WHEN 2 THEN d.ReviewedAt ELSE NULL END,
        d.Name,
        d.Description,
        NULL
    FROM Drafts d
    WHERE NOT EXISTS (
        SELECT 1
        FROM KnowledgeGraph.KnowledgeNodes existing
        WHERE existing.Id = d.Id
    );
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'KnowledgeGraph' AND TABLE_NAME = 'TagVersions')
BEGIN
    INSERT INTO KnowledgeGraph.Tags
        (Id, StableId, VersionNumber, Status, IsCurrent, PublishedAt, RetiredAt, CreatedAt, CreatedBy, ApprovedAt, ApprovedBy, Name, Description, DefaultL10nSetId)
    SELECT
        v.Id,
        COALESCE(t.StableId, v.TagId),
        CASE WHEN v.VersionNumber IS NULL OR v.VersionNumber = 0 THEN 1 ELSE v.VersionNumber END,
        'Archived',
        0,
        COALESCE(v.PublishedAt, v.CreatedDate),
        COALESCE(v.PublishedAt, v.CreatedDate),
        v.CreatedDate,
        COALESCE(NULLIF(v.PublishedBy, ''), NULLIF(t.CreatedBy, ''), 'system'),
        COALESCE(v.PublishedAt, v.CreatedDate),
        COALESCE(NULLIF(v.PublishedBy, ''), NULLIF(t.CreatedBy, ''), 'system'),
        v.Name,
        v.Description,
        NULL
    FROM KnowledgeGraph.TagVersions v
    LEFT JOIN KnowledgeGraph.Tags t ON t.Id = v.TagId
    WHERE NOT EXISTS (
        SELECT 1
        FROM KnowledgeGraph.Tags existing
        WHERE existing.Id = v.Id
    );
END
");

            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'KnowledgeGraph' AND TABLE_NAME = 'TagDrafts')
BEGIN
    WITH DraftBases AS (
        SELECT
            d.Id,
            d.TagId,
            d.Name,
            d.Description,
            d.ReviewStatus,
            d.SubmittedBy,
            d.SubmittedAt,
            d.ReviewedBy,
            d.ReviewedAt,
            COALESCE(t.StableId, d.TagId) AS StableId
        FROM KnowledgeGraph.TagDrafts d
        LEFT JOIN KnowledgeGraph.Tags t ON t.Id = d.TagId
    ),
    MaxVersions AS (
        SELECT StableId, MAX(VersionNumber) AS MaxVersion
        FROM KnowledgeGraph.Tags
        GROUP BY StableId
    ),
    Drafts AS (
        SELECT
            b.*,
            COALESCE(m.MaxVersion, 0) AS BaseVersion,
            ROW_NUMBER() OVER (PARTITION BY b.StableId ORDER BY b.SubmittedAt, b.Id) AS DraftOrder
        FROM DraftBases b
        LEFT JOIN MaxVersions m ON m.StableId = b.StableId
    )
    INSERT INTO KnowledgeGraph.Tags
        (Id, StableId, VersionNumber, Status, IsCurrent, CreatedAt, CreatedBy, PublishedAt, ApprovedAt, ApprovedBy, RetiredAt, Name, Description, DefaultL10nSetId)
    SELECT
        d.Id,
        d.StableId,
        d.BaseVersion + d.DraftOrder,
        CASE d.ReviewStatus WHEN 0 THEN 'Archived' WHEN 2 THEN 'Rejected' ELSE 'Draft' END,
        0,
        d.SubmittedAt,
        NULLIF(d.SubmittedBy, ''),
        CASE d.ReviewStatus WHEN 0 THEN COALESCE(d.ReviewedAt, d.SubmittedAt) ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN d.ReviewedAt WHEN 2 THEN d.ReviewedAt ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN NULLIF(d.ReviewedBy, '') WHEN 2 THEN NULLIF(d.ReviewedBy, '') ELSE NULL END,
        CASE d.ReviewStatus WHEN 0 THEN d.ReviewedAt WHEN 2 THEN d.ReviewedAt ELSE NULL END,
        d.Name,
        d.Description,
        NULL
    FROM Drafts d
    WHERE NOT EXISTS (
        SELECT 1
        FROM KnowledgeGraph.Tags existing
        WHERE existing.Id = d.Id
    );
END
");

            migrationBuilder.Sql(@"
IF OBJECT_ID('KnowledgeGraph.KnowledgeNodeDrafts', 'U') IS NOT NULL
    DROP TABLE KnowledgeGraph.KnowledgeNodeDrafts;
IF OBJECT_ID('KnowledgeGraph.KnowledgeNodeVersions', 'U') IS NOT NULL
    DROP TABLE KnowledgeGraph.KnowledgeNodeVersions;
IF OBJECT_ID('KnowledgeGraph.TagDrafts', 'U') IS NOT NULL
    DROP TABLE KnowledgeGraph.TagDrafts;
IF OBJECT_ID('KnowledgeGraph.TagVersions', 'U') IS NOT NULL
    DROP TABLE KnowledgeGraph.TagVersions;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KnowledgeNodeDrafts",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodeDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeNodeDrafts_KnowledgeNodes_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "KnowledgeNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodeDrafts_NodeId",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodeDrafts",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeNodeDrafts_NodeId",
                schema: "KnowledgeGraph",
                table: "KnowledgeNodeDrafts",
                column: "NodeId");

            migrationBuilder.CreateTable(
                name: "KnowledgeNodeVersions",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeNodeVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TagDrafts",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewStatus = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagDrafts_Tags_TagId",
                        column: x => x.TagId,
                        principalSchema: "KnowledgeGraph",
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagDrafts_TagId",
                schema: "KnowledgeGraph",
                table: "TagDrafts",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagDrafts_TagId",
                schema: "KnowledgeGraph",
                table: "TagDrafts",
                column: "TagId");

            migrationBuilder.CreateTable(
                name: "TagVersions",
                schema: "KnowledgeGraph",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PublishedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagVersions", x => x.Id);
                });

            migrationBuilder.Sql(@"
INSERT INTO KnowledgeGraph.KnowledgeNodeDrafts
    (Id, NodeId, Name, Description, ReviewStatus, SubmittedBy, SubmittedAt, ReviewedBy, ReviewedAt, ReviewComment)
SELECT
    Id,
    StableId,
    Name,
    Description,
    CASE Status WHEN 'Current' THEN 0 WHEN 'Rejected' THEN 2 ELSE 1 END,
    CreatedBy,
    COALESCE(CreatedAt, SYSUTCDATETIME()),
    ApprovedBy,
    ApprovedAt,
    NULL
FROM KnowledgeGraph.KnowledgeNodes
WHERE Status IN ('Draft','Rejected');

INSERT INTO KnowledgeGraph.TagDrafts
    (Id, TagId, Name, Description, ReviewStatus, SubmittedBy, SubmittedAt, ReviewedBy, ReviewedAt, ReviewComment)
SELECT
    Id,
    StableId,
    Name,
    Description,
    CASE Status WHEN 'Current' THEN 0 WHEN 'Rejected' THEN 2 ELSE 1 END,
    CreatedBy,
    COALESCE(CreatedAt, SYSUTCDATETIME()),
    ApprovedBy,
    ApprovedAt,
    NULL
FROM KnowledgeGraph.Tags
WHERE Status IN ('Draft','Rejected');

INSERT INTO KnowledgeGraph.KnowledgeNodeVersions
    (Id, NodeId, Name, Description, CreatedDate, PublishedBy, PublishedAt, VersionNumber)
SELECT
    Id,
    StableId,
    Name,
    Description,
    COALESCE(CreatedAt, SYSUTCDATETIME()),
    ApprovedBy,
    ApprovedAt,
    VersionNumber
FROM KnowledgeGraph.KnowledgeNodes;

INSERT INTO KnowledgeGraph.TagVersions
    (Id, TagId, Name, Description, CreatedDate, PublishedBy, PublishedAt, VersionNumber)
SELECT
    Id,
    StableId,
    Name,
    Description,
    COALESCE(CreatedAt, SYSUTCDATETIME()),
    ApprovedBy,
    ApprovedAt,
    VersionNumber
FROM KnowledgeGraph.Tags;

");
        }
    }
}
