using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sciencetopia.Data;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260516090000_AddSearchVectorDocuments")]
    public partial class AddSearchVectorDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF SCHEMA_ID(N'Search') IS NULL
    EXEC(N'CREATE SCHEMA [Search]');

IF OBJECT_ID(N'[Search].[VectorDocuments]', N'U') IS NULL
BEGIN
    CREATE TABLE [Search].[VectorDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [EntityType] nvarchar(32) NOT NULL,
        [EntityId] nvarchar(64) NOT NULL,
        [Title] nvarchar(512) NULL,
        [Content] nvarchar(max) NULL,
        [SourceHash] nvarchar(128) NOT NULL,
        [Provider] nvarchar(64) NOT NULL,
        [Model] nvarchar(128) NOT NULL,
        [VectorJson] nvarchar(max) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_SearchVectorDocuments_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        [IsDeleted] bit NOT NULL CONSTRAINT [DF_SearchVectorDocuments_IsDeleted] DEFAULT 0,
        CONSTRAINT [PK_SearchVectorDocuments] PRIMARY KEY ([Id])
    );

    CREATE UNIQUE INDEX [UX_SearchVectorDocuments_Entity_Provider_Model]
        ON [Search].[VectorDocuments] ([EntityType], [EntityId], [Provider], [Model]);

    CREATE INDEX [IX_SearchVectorDocuments_Type_UpdatedAt]
        ON [Search].[VectorDocuments] ([EntityType], [UpdatedAt]);
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[Search].[VectorDocuments]', N'U') IS NOT NULL
    DROP TABLE [Search].[VectorDocuments];
");
        }
    }
}
