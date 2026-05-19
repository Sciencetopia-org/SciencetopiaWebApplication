using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sciencetopia.Data;

#nullable disable

namespace SciencetopiaWebApplication.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260514090000_AddStudyGroupDiscoveryV1")]
    public partial class AddStudyGroupDiscoveryV1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StudyGroups].[StudyGroupRecommendationFeedback]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyGroups].[StudyGroupRecommendationFeedback] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        [GroupId] uniqueidentifier NOT NULL,
        [Action] nvarchar(32) NOT NULL,
        [Scene] nvarchar(32) NOT NULL,
        [Position] int NULL,
        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_SGRF_CreatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_StudyGroupRecommendationFeedback] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_StudyGroupRecommendationFeedback_StudyGroups_GroupId]
            FOREIGN KEY ([GroupId]) REFERENCES [StudyGroups].[StudyGroups]([Id]) ON DELETE CASCADE
    );

    CREATE INDEX [IX_StudyGroupRecommendationFeedback_RequestId]
        ON [StudyGroups].[StudyGroupRecommendationFeedback] ([RequestId]);
    CREATE INDEX [IX_StudyGroupRecommendationFeedback_UserId_CreatedAt]
        ON [StudyGroups].[StudyGroupRecommendationFeedback] ([UserId], [CreatedAt]);
    CREATE INDEX [IX_StudyGroupRecommendationFeedback_GroupId_Action_CreatedAt]
        ON [StudyGroups].[StudyGroupRecommendationFeedback] ([GroupId], [Action], [CreatedAt]);
END;

IF OBJECT_ID(N'[StudyGroups].[StudyGroupRecommendationRequestLogs]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyGroups].[StudyGroupRecommendationRequestLogs] (
        [Id] uniqueidentifier NOT NULL,
        [RequestId] uniqueidentifier NOT NULL,
        [UserId] nvarchar(450) COLLATE SQL_Latin1_General_CP1_CI_AS NULL,
        [Scene] nvarchar(32) NOT NULL,
        [SortMode] nvarchar(32) NOT NULL,
        [Query] nvarchar(512) NULL,
        [StrategyVersion] nvarchar(64) NOT NULL,
        [CandidateCount] int NOT NULL,
        [ReturnedCount] int NOT NULL,
        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_SGRRL_CreatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_StudyGroupRecommendationRequestLogs] PRIMARY KEY ([Id])
    );

    CREATE INDEX [IX_StudyGroupRecommendationRequestLogs_RequestId]
        ON [StudyGroups].[StudyGroupRecommendationRequestLogs] ([RequestId]);
    CREATE INDEX [IX_StudyGroupRecommendationRequestLogs_UserId_CreatedAt]
        ON [StudyGroups].[StudyGroupRecommendationRequestLogs] ([UserId], [CreatedAt]);
END;

IF OBJECT_ID(N'[StudyGroups].[StudyGroupEmbeddings]', N'U') IS NULL
BEGIN
    CREATE TABLE [StudyGroups].[StudyGroupEmbeddings] (
        [GroupId] uniqueidentifier NOT NULL,
        [Provider] nvarchar(64) NOT NULL,
        [Model] nvarchar(128) NOT NULL,
        [VectorStoreId] nvarchar(128) NULL,
        [SourceText] nvarchar(max) NOT NULL,
        [SourceHash] nvarchar(128) NOT NULL,
        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_SGE_UpdatedAt] DEFAULT SYSUTCDATETIME(),
        CONSTRAINT [PK_StudyGroupEmbeddings] PRIMARY KEY ([GroupId]),
        CONSTRAINT [FK_StudyGroupEmbeddings_StudyGroups_GroupId]
            FOREIGN KEY ([GroupId]) REFERENCES [StudyGroups].[StudyGroups]([Id]) ON DELETE CASCADE
    );
END;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[StudyGroups].[StudyGroupEmbeddings]', N'U') IS NOT NULL
    DROP TABLE [StudyGroups].[StudyGroupEmbeddings];

IF OBJECT_ID(N'[StudyGroups].[StudyGroupRecommendationRequestLogs]', N'U') IS NOT NULL
    DROP TABLE [StudyGroups].[StudyGroupRecommendationRequestLogs];

IF OBJECT_ID(N'[StudyGroups].[StudyGroupRecommendationFeedback]', N'U') IS NOT NULL
    DROP TABLE [StudyGroups].[StudyGroupRecommendationFeedback];
");
        }
    }
}
