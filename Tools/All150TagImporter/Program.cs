using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Neo4j.Driver;

namespace All150TagImporter;

internal static class Program
{
    private const int SqlCommandTimeoutSeconds = 30;
    private const int SqlApplyBatchSize = 25;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var workspaceRoot = FindWorkspaceRoot();
            var options = ImportOptions.Parse(args, workspaceRoot);
            var settings = LoadSettings(options.SettingsPath);

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine($"Input: {options.InputPath}");
            Console.WriteLine($"Settings: {options.SettingsPath}");
            Console.WriteLine($"Mode: {(options.Neo4jOnly ? "neo4j-only" : options.Apply ? "apply" : "dry-run")}");

            var rows = await LoadInputAsync(options.InputPath);
            var manifest = BuildManifest(rows);
            var existing = await ExistingSnapshot.LoadAsync(settings.ConnectionStrings.DefaultConnection);
            var resolver = new DuplicateResolver(existing, options.EnableFuzzyMerge);
            var resolution = resolver.Resolve(manifest.Candidates.Values.ToList());

            var preview = BuildPreview(rows, manifest, existing, resolution);
            Directory.CreateDirectory(Path.GetDirectoryName(options.PreviewPath)!);
            await File.WriteAllTextAsync(options.PreviewPath, JsonSerializer.Serialize(preview, JsonOptions));

            PrintPreviewSummary(preview, options.PreviewPath);

            if (!options.Apply && !options.Neo4jOnly)
            {
                return 0;
            }

            if (preview.AmbiguousMatches.Count > 0 && !options.AllowAmbiguousMatches)
            {
                Console.Error.WriteLine("Apply aborted because ambiguous duplicate matches were detected. Re-run after review or use --allow-ambiguous-matches.");
                return 2;
            }

            var applied = options.Neo4jOnly
                ? await ApplyNeo4jOnlyAsync(settings, manifest, resolution, existing)
                : await ApplyAsync(settings, options, manifest, resolution, existing);
            Console.WriteLine();
            Console.WriteLine(options.Neo4jOnly ? "Neo4j repair completed." : "Apply completed.");
            Console.WriteLine($"SQL created tags: {applied.CreatedTagCount}");
            Console.WriteLine($"SQL reused tags: {applied.ReusedTagCount}");
            Console.WriteLine($"SQL created representative nodes: {applied.CreatedRepresentativeNodeCount}");
            Console.WriteLine($"Neo4j projected tags: {applied.ProjectedTagCount}");
            Console.WriteLine($"Neo4j contain edges ensured: {applied.ContainEdgeCount}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static async Task<ApplyReport> ApplyNeo4jOnlyAsync(
        AppSettings settings,
        ImportManifest manifest,
        ResolutionResult resolution,
        ExistingSnapshot existing)
    {
        var report = new ApplyReport();
        var resolvedItems = resolution.ByCandidateKey.Values
            .OrderBy(x => LevelRank(x.Candidate.Level))
            .ThenBy(x => x.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        var graphRows = new List<GraphProjectionRow>();
        foreach (var item in resolvedItems)
        {
            if (!existing.TagsByStableId.TryGetValue(item.ResolvedTagStableId, out var current))
            {
                continue;
            }

            if (current.RepresentativeNodeVersionId is null || current.RepresentativeNodeStableId is null)
            {
                Console.WriteLine($"  Neo4j repair skip: {item.Candidate.Name} has no SQL representative node yet.");
                continue;
            }

            graphRows.Add(new GraphProjectionRow
            {
                TagStableId = current.TagStableId.ToString("D"),
                TagVersionId = current.TagVersionId.ToString("D"),
                NodeStableId = current.RepresentativeNodeStableId.Value.ToString("D"),
                NodeVersionId = current.RepresentativeNodeVersionId.Value.ToString("D"),
                Level = item.Candidate.Level
            });
        }

        var projectedTagIds = graphRows
            .Select(x => Guid.Parse(x.TagStableId))
            .ToHashSet();

        var containEdges = manifest.Edges
            .Select(edge => new ResolvedEdge(
                resolution.ByCandidateKey[edge.ParentKey].ResolvedTagStableId,
                resolution.ByCandidateKey[edge.ChildKey].ResolvedTagStableId))
            .Where(edge => edge.ParentStableId != edge.ChildStableId)
            .Where(edge => projectedTagIds.Contains(edge.ParentStableId) && projectedTagIds.Contains(edge.ChildStableId))
            .Distinct()
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Starting Neo4j-only repair for {graphRows.Count} SQL-backed tag nodes and {containEdges.Count} contain edges...");
        if (graphRows.Count == 0)
        {
            Console.WriteLine("No SQL-backed rows are available for Neo4j repair.");
            return report;
        }

        await ApplyNeo4jProjectionAsync(settings.Neo4j, graphRows, containEdges);
        report.ProjectedTagCount = graphRows.Count;
        report.ContainEdgeCount = containEdges.Count;
        report.ReusedTagCount = graphRows.Count;
        return report;
    }

    private static void PrintPreviewSummary(PreviewReport preview, string previewPath)
    {
        Console.WriteLine();
        Console.WriteLine("Preview summary");
        Console.WriteLine($"- Source rows: {preview.SourceRowCount}");
        Console.WriteLine($"- Unique import terms after level correction: {preview.UniqueImportTagCount}");
        Console.WriteLine($"- Existing exact matches: {preview.ExactMatchCount}");
        Console.WriteLine($"- Existing fuzzy matches: {preview.FuzzyMatchCount}");
        Console.WriteLine($"- New tags to create: {preview.NewTagCount}");
        Console.WriteLine($"- Hierarchy edges to ensure: {preview.ContainEdgeCount}");
        Console.WriteLine($"- Ambiguous matches: {preview.AmbiguousMatches.Count}");
        Console.WriteLine($"- Preview file: {previewPath}");

        if (preview.LevelCounts.Count > 0)
        {
            Console.WriteLine("- Level counts:");
            foreach (var item in preview.LevelCounts.OrderBy(kvp => LevelRank(kvp.Key)))
            {
                Console.WriteLine($"  {item.Key}: {item.Value}");
            }
        }

        if (preview.AmbiguousMatches.Count > 0)
        {
            Console.WriteLine("- Ambiguous examples:");
            foreach (var item in preview.AmbiguousMatches.Take(10))
            {
                Console.WriteLine($"  {item.ImportName} -> {string.Join(", ", item.Candidates.Select(c => $"{c.Name} ({c.Score:F2})"))}");
            }
        }
    }

    private static PreviewReport BuildPreview(
        IReadOnlyList<InputRow> rows,
        ImportManifest manifest,
        ExistingSnapshot existing,
        ResolutionResult resolution)
    {
        var containEdges = manifest.Edges
            .Select(edge => new ResolvedEdge(
                resolution.ByCandidateKey[edge.ParentKey].ResolvedTagStableId,
                resolution.ByCandidateKey[edge.ChildKey].ResolvedTagStableId))
            .Where(edge => edge.ParentStableId != edge.ChildStableId)
            .Distinct()
            .ToList();

        return new PreviewReport
        {
            SourceRowCount = rows.Count,
            UniqueImportTagCount = manifest.Candidates.Count,
            ExactMatchCount = resolution.ByCandidateKey.Values.Count(x => x.MatchKind == MatchKind.Exact),
            FuzzyMatchCount = resolution.ByCandidateKey.Values.Count(x => x.MatchKind == MatchKind.Fuzzy),
            NewTagCount = resolution.ByCandidateKey.Values.Count(x => x.MatchKind == MatchKind.New),
            ContainEdgeCount = containEdges.Count,
            ExistingCurrentTagCount = existing.TagsByStableId.Count,
            LevelCounts = manifest.Candidates.Values
                .GroupBy(x => x.Level)
                .OrderBy(g => LevelRank(g.Key))
                .ToDictionary(g => g.Key, g => g.Count()),
            AmbiguousMatches = resolution.AmbiguousMatches
                .Select(x => new AmbiguousMatchReport
                {
                    ImportName = x.Candidate.Name,
                    Level = x.Candidate.Level,
                    Candidates = x.Candidates
                        .Select(y => new MatchCandidateReport { Name = y.Name, Score = y.Score })
                        .ToList()
                })
                .ToList(),
            SampleNewTags = resolution.ByCandidateKey.Values
                .Where(x => x.MatchKind == MatchKind.New)
                .OrderBy(x => x.Candidate.Level)
                .ThenBy(x => x.Candidate.Name, StringComparer.Ordinal)
                .Take(30)
                .Select(x => new TagPreviewItem
                {
                    Name = x.Candidate.Name,
                    Level = x.Candidate.Level,
                    Description = x.Candidate.Description
                })
                .ToList()
        };
    }

    private static async Task<ApplyReport> ApplyAsync(
        AppSettings settings,
        ImportOptions options,
        ImportManifest manifest,
        ResolutionResult resolution,
        ExistingSnapshot existing)
    {
        var report = new ApplyReport();
        var importedTags = resolution.ByCandidateKey.Values
            .OrderBy(x => LevelRank(x.Candidate.Level))
            .ThenBy(x => x.Candidate.Name, StringComparer.Ordinal)
            .ToList();

        var containEdges = manifest.Edges
            .Select(edge => new ResolvedEdge(
                resolution.ByCandidateKey[edge.ParentKey].ResolvedTagStableId,
                resolution.ByCandidateKey[edge.ChildKey].ResolvedTagStableId))
            .Where(edge => edge.ParentStableId != edge.ChildStableId)
            .Distinct()
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"Starting SQL apply for {importedTags.Count} tags in batches of {SqlApplyBatchSize}...");

        var processedSqlTags = 0;
        for (var batchStart = 0; batchStart < importedTags.Count; batchStart += SqlApplyBatchSize)
        {
            var batch = importedTags
                .Skip(batchStart)
                .Take(SqlApplyBatchSize)
                .ToList();
            var batchNumber = (batchStart / SqlApplyBatchSize) + 1;
            var batchEnd = batchStart + batch.Count;
            var sqlStage = $"batch {batchNumber} not started";
            var sqlItemIndex = 0;
            var sqlItemName = "";

            var batchReport = await WithSqlConnectionAsync(settings.ConnectionStrings.DefaultConnection, async connection =>
            {
                sqlStage = $"batch {batchNumber} opening transaction";
                sqlItemIndex = 0;
                sqlItemName = "";
                Console.WriteLine($"  SQL: opening transaction for batch {batchNumber} ({batchStart + 1}-{batchEnd}/{importedTags.Count})...");
                await using var tx = (SqlTransaction)await connection.BeginTransactionAsync();

                sqlStage = $"batch {batchNumber} loading tag types";
                Console.WriteLine($"  SQL: loading tag types for batch {batchNumber}...");
                var typeIds = await LoadTagTypeIdsAsync(connection, tx);
                if (!typeIds.TryGetValue("MainTag", out var mainTagTypeId))
                {
                    throw new InvalidOperationException("KnowledgeGraph.TypesOfTags is missing MainTag.");
                }
                Console.WriteLine($"  SQL: tag types loaded ({typeIds.Count} rows).");

                var batchResult = new ApplyReport();
                for (var index = 0; index < batch.Count; index++)
                {
                    var item = batch[index];
                    var globalIndex = batchStart + index + 1;
                    sqlStage = $"batch {batchNumber} processing tag {globalIndex}/{importedTags.Count}";
                    sqlItemIndex = globalIndex;
                    sqlItemName = item.Candidate.Name;

                    if (index == 0)
                    {
                        Console.WriteLine($"  SQL: processing {item.Candidate.Name} [{item.MatchKind}]...");
                    }

                    if (item.MatchKind == MatchKind.New)
                    {
                        item.TagVersionId = Guid.NewGuid();
                        item.RepresentativeNodeVersionId = Guid.NewGuid();
                        item.RepresentativeNodeStableId = Guid.NewGuid();

                        await InsertCurrentTagAsync(connection, tx, item.TagVersionId.Value, item.ResolvedTagStableId, item.Candidate, options.SystemUser);
                        await UpsertTagTypeAsync(connection, tx, item.ResolvedTagStableId, mainTagTypeId);
                        await UpsertL10nAsync(connection, tx, EntityScope.Tag, item.TagVersionId.Value, "name", "zh", item.Candidate.Name);
                        await UpsertL10nAsync(connection, tx, EntityScope.Tag, item.TagVersionId.Value, "description", "zh", item.Candidate.Description);

                        await InsertCurrentKnowledgeNodeAsync(connection, tx, item.RepresentativeNodeVersionId.Value, item.RepresentativeNodeStableId.Value, item.Candidate, options.SystemUser);
                        await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "name", "zh", item.Candidate.Name);
                        await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "description", "zh", item.Candidate.Description);
                        await EnsureRepresentativeLinkAsync(connection, tx, item.TagVersionId.Value, item.RepresentativeNodeVersionId.Value);

                        batchResult.CreatedTagCount++;
                        batchResult.CreatedRepresentativeNodeCount++;
                    }
                    else
                    {
                        var current = existing.TagsByStableId[item.ResolvedTagStableId];
                        item.TagVersionId = current.TagVersionId;

                        await UpsertTagTypeAsync(connection, tx, item.ResolvedTagStableId, mainTagTypeId);
                        await UpsertL10nAsync(connection, tx, EntityScope.Tag, current.TagVersionId, "name", "zh", item.Candidate.Name);
                        await UpsertL10nAsync(connection, tx, EntityScope.Tag, current.TagVersionId, "description", "zh", item.Candidate.Description);

                        if (current.RepresentativeNodeVersionId is null || current.RepresentativeNodeStableId is null)
                        {
                            item.RepresentativeNodeVersionId = Guid.NewGuid();
                            item.RepresentativeNodeStableId = Guid.NewGuid();

                            await InsertCurrentKnowledgeNodeAsync(connection, tx, item.RepresentativeNodeVersionId.Value, item.RepresentativeNodeStableId.Value, item.Candidate, options.SystemUser);
                            await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "name", "zh", item.Candidate.Name);
                            await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "description", "zh", item.Candidate.Description);
                            await EnsureRepresentativeLinkAsync(connection, tx, current.TagVersionId, item.RepresentativeNodeVersionId.Value);
                            batchResult.CreatedRepresentativeNodeCount++;
                        }
                        else
                        {
                            item.RepresentativeNodeVersionId = current.RepresentativeNodeVersionId.Value;
                            item.RepresentativeNodeStableId = current.RepresentativeNodeStableId.Value;
                            await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "name", "zh", item.Candidate.Name);
                            await UpsertL10nAsync(connection, tx, EntityScope.Node, item.RepresentativeNodeVersionId.Value, "description", "zh", item.Candidate.Description);
                        }

                        batchResult.ReusedTagCount++;
                    }
                }

                sqlStage = $"batch {batchNumber} committing SQL transaction";
                sqlItemIndex = batchEnd;
                sqlItemName = "";
                Console.WriteLine($"  SQL: committing batch {batchNumber}...");
                await tx.CommitAsync();
                return batchResult;
            }, () =>
            {
                if (!string.IsNullOrWhiteSpace(sqlItemName))
                {
                    return $"{sqlStage}; current tag: {sqlItemName} ({sqlItemIndex}/{importedTags.Count}); batch {batchNumber} covers {batchStart + 1}-{batchEnd}";
                }

                return $"{sqlStage}; batch {batchNumber} covers {batchStart + 1}-{batchEnd}";
            });

            report.CreatedTagCount += batchReport.CreatedTagCount;
            report.ReusedTagCount += batchReport.ReusedTagCount;
            report.CreatedRepresentativeNodeCount += batchReport.CreatedRepresentativeNodeCount;
            processedSqlTags += batch.Count;
            Console.WriteLine($"  SQL progress: {processedSqlTags}/{importedTags.Count}");
        }

        var graphRows = importedTags.Select(x => new GraphProjectionRow
        {
            TagStableId = x.ResolvedTagStableId.ToString("D"),
            TagVersionId = x.TagVersionId!.Value.ToString("D"),
            NodeStableId = x.RepresentativeNodeStableId!.Value.ToString("D"),
            NodeVersionId = x.RepresentativeNodeVersionId!.Value.ToString("D"),
            Level = x.Candidate.Level
        }).ToList();

        Console.WriteLine($"Starting Neo4j projection for {graphRows.Count} tag nodes and {containEdges.Count} contain edges...");
        await ApplyNeo4jProjectionAsync(settings.Neo4j, graphRows, containEdges);
        Console.WriteLine("Neo4j projection finished.");

        report.ProjectedTagCount = graphRows.Count;
        report.ContainEdgeCount = containEdges.Count;
        return report;
    }

    private static async Task ApplyNeo4jProjectionAsync(
        Neo4jSettings settings,
        IReadOnlyList<GraphProjectionRow> graphRows,
        IReadOnlyList<ResolvedEdge> containEdges)
    {
        await using var driver = GraphDatabase.Driver(settings.Uri, AuthTokens.Basic(settings.User, settings.Password));
        Console.WriteLine("  Neo4j: verifying connectivity...");
        await driver.VerifyConnectivityAsync();
        Console.WriteLine("  Neo4j: connectivity verified.");

        await using var session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write).WithDatabase("neo4j"));

        await session.ExecuteWriteAsync(async tx =>
        {
            var query = """
                UNWIND $rows AS row
                MERGE (t:Tag {stableId: row.tagStableId})
                SET t.currentVersionId = row.tagVersionId,
                    t.status = 'Current'
                MERGE (n:KnowledgeNode {stableId: row.nodeStableId})
                SET n.currentVersionId = row.nodeVersionId,
                    n.status = 'Current'
                MERGE (t)-[tagged:TAGGED_WITH]->(n)
                SET tagged.status = 'Current'
                MERGE (lvl:TagLevel {name: row.level})
                SET lvl.id = row.level
                WITH n, lvl, row
                OPTIONAL MATCH (old:TagLevel)-[oldRel:TAGGED_WITH]->(n)
                WHERE old.name <> row.level
                DELETE oldRel
                MERGE (lvl)-[:TAGGED_WITH]->(n)
                """;

            Console.WriteLine("  Neo4j: writing tag/node projection...");
            var rowCursor = await tx.RunAsync(query, new
            {
                rows = graphRows.Select(x => new
                {
                    tagStableId = x.TagStableId,
                    tagVersionId = x.TagVersionId,
                    nodeStableId = x.NodeStableId,
                    nodeVersionId = x.NodeVersionId,
                    level = x.Level
                }).ToArray()
            });
            await rowCursor.ConsumeAsync();
            Console.WriteLine("  Neo4j: tag/node projection complete.");

            if (containEdges.Count > 0)
            {
                var edgeQuery = """
                    UNWIND $edges AS edge
                    MATCH (parent:Tag {stableId: edge.parentStableId})
                    MATCH (child:Tag {stableId: edge.childStableId})
                    WHERE parent.stableId <> child.stableId
                    MERGE (parent)-[:CONTAIN]->(child)
                    """;

                Console.WriteLine("  Neo4j: writing contain edges...");
                var edgeCursor = await tx.RunAsync(edgeQuery, new
                {
                    edges = containEdges.Select(x => new
                    {
                        parentStableId = x.ParentStableId.ToString("D"),
                        childStableId = x.ChildStableId.ToString("D")
                    }).ToArray()
                });
                await edgeCursor.ConsumeAsync();
                Console.WriteLine("  Neo4j: contain edges complete.");
            }
        });
    }

    private static async Task<Dictionary<string, int>> LoadTagTypeIdsAsync(SqlConnection connection, SqlTransaction tx)
    {
        const string sql = "SELECT [Type], [Id] FROM [KnowledgeGraph].[TypesOfTags];";
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = CreateSqlCommand(sql, connection, tx);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var type = reader.GetString(0);
            var id = reader.GetInt32(1);
            result[type] = id;
        }

        return result;
    }

    private static async Task InsertCurrentTagAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid tagVersionId,
        Guid tagStableId,
        ImportCandidate candidate,
        string actor)
    {
        const string sql = """
            INSERT INTO [KnowledgeGraph].[Tags]
            (
                [Id], [StableId], [VersionNumber], [Status], [IsCurrent],
                [PublishedAt], [RetiredAt], [CreatedAt], [CreatedBy], [ApprovedAt], [ApprovedBy],
                [Name], [Description]
            )
            VALUES
            (
                @Id, @StableId, 1, 'Current', 1,
                @Now, NULL, @Now, @Actor, @Now, @Actor,
                @Name, @Description
            );
            """;

        var now = DateTimeOffset.UtcNow;
        await using var cmd = CreateSqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Id", tagVersionId);
        cmd.Parameters.AddWithValue("@StableId", tagStableId);
        cmd.Parameters.AddWithValue("@Now", now);
        cmd.Parameters.AddWithValue("@Actor", actor);
        cmd.Parameters.AddWithValue("@Name", (object?)candidate.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Description", (object?)candidate.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertCurrentKnowledgeNodeAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid nodeVersionId,
        Guid nodeStableId,
        ImportCandidate candidate,
        string actor)
    {
        const string sql = """
            INSERT INTO [KnowledgeGraph].[KnowledgeNodes]
            (
                [Id], [StableId], [VersionNumber], [Status], [IsCurrent],
                [PublishedAt], [RetiredAt], [CreatedAt], [CreatedBy], [ApprovedAt], [ApprovedBy],
                [Name], [Description]
            )
            VALUES
            (
                @Id, @StableId, 1, 'Current', 1,
                @Now, NULL, @Now, @Actor, @Now, @Actor,
                @Name, @Description
            );
            """;

        var now = DateTimeOffset.UtcNow;
        await using var cmd = CreateSqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@Id", nodeVersionId);
        cmd.Parameters.AddWithValue("@StableId", nodeStableId);
        cmd.Parameters.AddWithValue("@Now", now);
        cmd.Parameters.AddWithValue("@Actor", actor);
        cmd.Parameters.AddWithValue("@Name", (object?)candidate.Name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Description", (object?)candidate.Description ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertTagTypeAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid tagStableId,
        int mainTagTypeId)
    {
        const string sql = """
            MERGE [KnowledgeGraph].[TagTypes] AS target
            USING (SELECT @TagStableId AS TagStableId, @TypeId AS TypeId) AS source
            ON target.[TagStableId] = source.[TagStableId]
            WHEN MATCHED THEN UPDATE SET [TypeId] = source.[TypeId]
            WHEN NOT MATCHED THEN
                INSERT ([TagStableId], [TypeId]) VALUES (source.[TagStableId], source.[TypeId]);
            """;

        await using var cmd = CreateSqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@TagStableId", tagStableId);
        cmd.Parameters.AddWithValue("@TypeId", mainTagTypeId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task EnsureRepresentativeLinkAsync(
        SqlConnection connection,
        SqlTransaction tx,
        Guid tagVersionId,
        Guid nodeVersionId)
    {
        const string sql = """
            IF NOT EXISTS
            (
                SELECT 1
                FROM [KnowledgeGraph].[TagRepresentativeNode]
                WHERE [TagId] = @TagId AND [NodeId] = @NodeId
            )
            BEGIN
                INSERT INTO [KnowledgeGraph].[TagRepresentativeNode] ([TagId], [NodeId])
                VALUES (@TagId, @NodeId);
            END
            """;

        await using var cmd = CreateSqlCommand(sql, connection, tx);
        cmd.Parameters.AddWithValue("@TagId", tagVersionId);
        cmd.Parameters.AddWithValue("@NodeId", nodeVersionId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertL10nAsync(
        SqlConnection connection,
        SqlTransaction tx,
        EntityScope scope,
        Guid entityVersionId,
        string fieldKey,
        string langCode,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var setId = await EnsurePrimaryL10nSetAsync(connection, tx, scope, entityVersionId);
        const string findSql = """
            SELECT TOP 1 i.[L10nItemId]
            FROM [L10n].[L10nSetItems] si
            JOIN [L10n].[L10nItems] i ON i.[L10nItemId] = si.[L10nItemId]
            WHERE si.[L10nSetId] = @SetId
              AND i.[FieldKey] = @FieldKey
              AND i.[LangCode] = @LangCode
            ORDER BY i.[SortOrder], i.[L10nItemId];
            """;

        Guid? itemId = null;
        await using (var findCmd = CreateSqlCommand(findSql, connection, tx))
        {
            findCmd.Parameters.AddWithValue("@SetId", setId);
            findCmd.Parameters.AddWithValue("@FieldKey", fieldKey);
            findCmd.Parameters.AddWithValue("@LangCode", langCode);
            var scalar = await findCmd.ExecuteScalarAsync();
            if (scalar is Guid guid)
            {
                itemId = guid;
            }
        }

        var useContent = string.Equals(fieldKey, "description", StringComparison.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        if (itemId.HasValue)
        {
            var updateSql = useContent
                ? """
                    UPDATE [L10n].[L10nItems]
                    SET [Content] = @Value, [Text] = NULL, [Kind] = 0, [SortOrder] = 0, [UpdatedAt] = @Now
                    WHERE [L10nItemId] = @ItemId;
                    """
                : """
                    UPDATE [L10n].[L10nItems]
                    SET [Text] = @Value, [Content] = NULL, [Kind] = 0, [SortOrder] = 0, [UpdatedAt] = @Now
                    WHERE [L10nItemId] = @ItemId;
                    """;

            await using var updateCmd = CreateSqlCommand(updateSql, connection, tx);
            updateCmd.Parameters.AddWithValue("@Value", value);
            updateCmd.Parameters.AddWithValue("@Now", now);
            updateCmd.Parameters.AddWithValue("@ItemId", itemId.Value);
            await updateCmd.ExecuteNonQueryAsync();
            return;
        }

        var newItemId = Guid.NewGuid();
        var insertSql = useContent
            ? """
                INSERT INTO [L10n].[L10nItems]
                (
                    [L10nItemId], [FieldKey], [LangCode], [ScriptCode], [Kind],
                    [Text], [Content], [SortOrder], [CreatedAt], [UpdatedAt]
                )
                VALUES
                (
                    @ItemId, @FieldKey, @LangCode, NULL, 0,
                    NULL, @Value, 0, @Now, @Now
                );
                """
            : """
                INSERT INTO [L10n].[L10nItems]
                (
                    [L10nItemId], [FieldKey], [LangCode], [ScriptCode], [Kind],
                    [Text], [Content], [SortOrder], [CreatedAt], [UpdatedAt]
                )
                VALUES
                (
                    @ItemId, @FieldKey, @LangCode, NULL, 0,
                    @Value, NULL, 0, @Now, @Now
                );
                """;

        await using (var insertCmd = CreateSqlCommand(insertSql, connection, tx))
        {
            insertCmd.Parameters.AddWithValue("@ItemId", newItemId);
            insertCmd.Parameters.AddWithValue("@FieldKey", fieldKey);
            insertCmd.Parameters.AddWithValue("@LangCode", langCode);
            insertCmd.Parameters.AddWithValue("@Value", value);
            insertCmd.Parameters.AddWithValue("@Now", now);
            await insertCmd.ExecuteNonQueryAsync();
        }

        const string mapSql = """
            INSERT INTO [L10n].[L10nSetItems] ([L10nSetId], [L10nItemId], [CreatedAt])
            VALUES (@SetId, @ItemId, @Now);
            """;

        await using var mapCmd = CreateSqlCommand(mapSql, connection, tx);
        mapCmd.Parameters.AddWithValue("@SetId", setId);
        mapCmd.Parameters.AddWithValue("@ItemId", newItemId);
        mapCmd.Parameters.AddWithValue("@Now", now);
        await mapCmd.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> EnsurePrimaryL10nSetAsync(
        SqlConnection connection,
        SqlTransaction tx,
        EntityScope scope,
        Guid entityVersionId)
    {
        var entityTable = scope == EntityScope.Tag ? "[KnowledgeGraph].[Tags]" : "[KnowledgeGraph].[KnowledgeNodes]";
        var linkTable = scope == EntityScope.Tag ? "[L10n].[TagL10nSets]" : "[L10n].[NodeL10nSets]";
        var linkColumn = scope == EntityScope.Tag ? "[TagId]" : "[NodeId]";
        var scopeName = scope == EntityScope.Tag ? "tag" : "knowledge_node";

        var selectSql = $"SELECT [DefaultL10nSetId] FROM {entityTable} WHERE [Id] = @Id;";
        Guid? setId = null;
        await using (var selectCmd = CreateSqlCommand(selectSql, connection, tx))
        {
            selectCmd.Parameters.AddWithValue("@Id", entityVersionId);
            var scalar = await selectCmd.ExecuteScalarAsync();
            if (scalar != null && scalar != DBNull.Value)
            {
                setId = (Guid)scalar;
            }
        }

        if (setId.HasValue)
        {
            return setId.Value;
        }

        setId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        const string insertSetSql = """
            INSERT INTO [L10n].[L10nSets]
            ([L10nSetId], [Scope], [PolicyJson], [IsManaged], [CreatedAt], [UpdatedAt])
            VALUES
            (@L10nSetId, @Scope, NULL, 0, @Now, @Now);
            """;

        await using (var insertSetCmd = CreateSqlCommand(insertSetSql, connection, tx))
        {
            insertSetCmd.Parameters.AddWithValue("@L10nSetId", setId.Value);
            insertSetCmd.Parameters.AddWithValue("@Scope", scopeName);
            insertSetCmd.Parameters.AddWithValue("@Now", now);
            await insertSetCmd.ExecuteNonQueryAsync();
        }

        var updateEntitySql = $"UPDATE {entityTable} SET [DefaultL10nSetId] = @L10nSetId WHERE [Id] = @Id;";
        await using (var updateEntityCmd = CreateSqlCommand(updateEntitySql, connection, tx))
        {
            updateEntityCmd.Parameters.AddWithValue("@L10nSetId", setId.Value);
            updateEntityCmd.Parameters.AddWithValue("@Id", entityVersionId);
            await updateEntityCmd.ExecuteNonQueryAsync();
        }

        var linkSql = $"INSERT INTO {linkTable} ({linkColumn}, [L10nSetId], [Relation], [CreatedAt]) VALUES (@Id, @L10nSetId, 0, @Now);";
        await using var linkCmd = CreateSqlCommand(linkSql, connection, tx);
        linkCmd.Parameters.AddWithValue("@Id", entityVersionId);
        linkCmd.Parameters.AddWithValue("@L10nSetId", setId.Value);
        linkCmd.Parameters.AddWithValue("@Now", now);
        await linkCmd.ExecuteNonQueryAsync();

        return setId.Value;
    }

    private static async Task<T> WithSqlConnectionAsync<T>(
        string connectionString,
        Func<SqlConnection, Task<T>> work,
        Func<string>? getDiagnosticContext = null)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await using var connection = new SqlConnection(connectionString);
            try
            {
                Console.WriteLine($"  SQL: opening connection (attempt {attempt}/4)...");
                await connection.OpenAsync();
                Console.WriteLine("  SQL: connection opened.");
                return await work(connection);
            }
            catch (Exception ex)
            {
                last = ex;
                Console.WriteLine($"  SQL: attempt {attempt}/4 failed.");
                var context = getDiagnosticContext?.Invoke();
                if (!string.IsNullOrWhiteSpace(context))
                {
                    Console.WriteLine($"  SQL failure context: {context}");
                }

                PrintSqlFailureDetails(ex);
                if (attempt == 4)
                {
                    break;
                }

                var delay = TimeSpan.FromSeconds(attempt * 3);
                Console.WriteLine($"  SQL: rolling back this batch transaction and retrying the current SQL unit after {delay.TotalSeconds:0}s.");
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException("SQL operation failed after retries.", last);
    }

    private static void PrintSqlFailureDetails(Exception ex)
    {
        Console.WriteLine($"  Exception: {ex.GetType().Name}: {ex.Message}");

        if (ex is SqlException sqlEx)
        {
            foreach (SqlError error in sqlEx.Errors)
            {
                Console.WriteLine(
                    $"  SqlError Number={error.Number}, State={error.State}, Class={error.Class}, Procedure={error.Procedure}, Line={error.LineNumber}: {error.Message}");
            }
        }

        var inner = ex.InnerException;
        var depth = 1;
        while (inner != null && depth <= 4)
        {
            Console.WriteLine($"  Inner[{depth}]: {inner.GetType().Name}: {inner.Message}");
            if (inner is SqlException innerSql)
            {
                foreach (SqlError error in innerSql.Errors)
                {
                    Console.WriteLine(
                        $"  Inner[{depth}] SqlError Number={error.Number}, State={error.State}, Class={error.Class}, Procedure={error.Procedure}, Line={error.LineNumber}: {error.Message}");
                }
            }

            inner = inner.InnerException;
            depth++;
        }
    }

    private static SqlCommand CreateSqlCommand(string sql, SqlConnection connection, SqlTransaction? tx = null)
    {
        return new SqlCommand(sql, connection, tx)
        {
            CommandTimeout = SqlCommandTimeoutSeconds
        };
    }

    private static async Task<List<InputRow>> LoadInputAsync(string inputPath)
    {
        await using var stream = File.OpenRead(inputPath);
        var rows = await JsonSerializer.DeserializeAsync<List<InputRow>>(stream, JsonOptions);
        return rows ?? new List<InputRow>();
    }

    private static ImportManifest BuildManifest(IReadOnlyList<InputRow> rows)
    {
        var candidates = new Dictionary<string, ImportCandidate>(StringComparer.Ordinal);
        var edges = new HashSet<ManifestEdge>();

        foreach (var row in rows)
        {
            var levels = BuildLevels(row);
            for (var index = 0; index < levels.Count; index++)
            {
                var level = levels[index];
                if (string.IsNullOrWhiteSpace(level.Name))
                {
                    continue;
                }

                var key = NormalizeName(level.Name);
                if (!candidates.TryGetValue(key, out var candidate))
                {
                    candidate = new ImportCandidate
                    {
                        Key = key,
                        Name = level.Name.Trim(),
                        Level = level.Level,
                        Description = level.Description?.Trim()
                    };
                    candidates[key] = candidate;
                }
                else
                {
                    if (LevelRank(level.Level) < LevelRank(candidate.Level))
                    {
                        candidate.Level = level.Level;
                    }

                    if (Length(level.Description) > Length(candidate.Description))
                    {
                        candidate.Description = level.Description?.Trim();
                    }
                }

                if (index > 0)
                {
                    var parent = levels[index - 1];
                    if (!string.IsNullOrWhiteSpace(parent.Name))
                    {
                        edges.Add(new ManifestEdge(NormalizeName(parent.Name), key));
                    }
                }
            }
        }

        return new ImportManifest
        {
            Candidates = candidates,
            Edges = edges
        };
    }

    private static List<LevelSeed> BuildLevels(InputRow row)
    {
        var detail = row.DataDetail ?? new Dictionary<string, string>();
        string? GetDescription(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return detail.TryGetValue(name, out var description) ? description : null;
        }

        var isStem = string.Equals(row.Discipline, "理科", StringComparison.Ordinal)
            || string.Equals(row.Discipline, "工科", StringComparison.Ordinal);

        if (isStem)
        {
            return new List<LevelSeed>
            {
                new("Discipline", row.Discipline, GetDescription(row.Discipline)),
                new("Subject", row.Subject, GetDescription(row.Subject)),
                new("Topic", row.Field, GetDescription(row.Field)),
                new("Keyword", row.Topic, GetDescription(row.Topic))
            };
        }

        return new List<LevelSeed>
        {
            new("Discipline", row.Discipline, GetDescription(row.Discipline)),
            new("Subject", row.Subject, GetDescription(row.Subject)),
            new("Field", row.Field, GetDescription(row.Field)),
            new("Topic", row.Topic, GetDescription(row.Topic))
        };
    }

    private static int Length(string? value) => value?.Trim().Length ?? 0;

    private static int LevelRank(string level) => level switch
    {
        "Keyword" => 1,
        "Topic" => 2,
        "Field" => 3,
        "Subject" => 4,
        "Discipline" => 5,
        _ => 0
    };

    private static string NormalizeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    private static string StripFuzzySuffix(string value)
    {
        var normalized = NormalizeName(value);
        return normalized.EndsWith("性", StringComparison.Ordinal)
            ? normalized[..^1]
            : normalized;
    }

    private static string? TryReadString(JsonElement? root, string sectionName, string propertyName)
    {
        if (!root.HasValue)
        {
            return null;
        }

        if (!root.Value.TryGetProperty(sectionName, out var section))
        {
            return null;
        }

        if (!section.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.GetString();
    }

    private static Guid? GetNullableGuid(IDataRecord record, string columnName)
    {
        var ordinal = record.GetOrdinal(columnName);
        return record.IsDBNull(ordinal) ? null : record.GetGuid(ordinal);
    }

    private static int? GetNullableInt32(IDataRecord record, string columnName)
    {
        var ordinal = record.GetOrdinal(columnName);
        return record.IsDBNull(ordinal) ? null : record.GetInt32(ordinal);
    }

    private static AppSettings LoadSettings(string settingsPath)
    {
        using var primary = JsonDocument.Parse(File.ReadAllText(settingsPath));
        JsonDocument? fallback = null;
        var fallbackPath = Path.Combine(Path.GetDirectoryName(settingsPath) ?? string.Empty, "appsettings.json");
        if (!string.Equals(Path.GetFullPath(settingsPath), Path.GetFullPath(fallbackPath), StringComparison.OrdinalIgnoreCase)
            && File.Exists(fallbackPath))
        {
            fallback = JsonDocument.Parse(File.ReadAllText(fallbackPath));
        }

        var root = primary.RootElement;
        var fallbackRoot = fallback?.RootElement;
        var connectionString = TryReadString(root, "ConnectionStrings", "DefaultConnection")
            ?? TryReadString(fallbackRoot, "ConnectionStrings", "DefaultConnection")
            ?? throw new InvalidOperationException("DefaultConnection is missing.");
        var uri = TryReadString(root, "Neo4j", "Uri")
            ?? TryReadString(fallbackRoot, "Neo4j", "Uri")
            ?? throw new InvalidOperationException("Neo4j.Uri is missing.");
        var user = TryReadString(root, "Neo4j", "User")
            ?? TryReadString(fallbackRoot, "Neo4j", "User")
            ?? throw new InvalidOperationException("Neo4j.User is missing.");
        var password = TryReadString(root, "Neo4j", "Password")
            ?? TryReadString(fallbackRoot, "Neo4j", "Password")
            ?? throw new InvalidOperationException("Neo4j.Password is missing.");

        return new AppSettings
        {
            ConnectionStrings = new ConnectionStrings { DefaultConnection = connectionString },
            Neo4j = new Neo4jSettings { Uri = uri, User = user, Password = password }
        };
    }

    private static string FindWorkspaceRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "SciencetopiaWebApplication", "SciencetopiaWebApplication.csproj");
            if (File.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the workspace root containing SciencetopiaWebApplication.csproj.");
    }

    private sealed class DuplicateResolver
    {
        private readonly ExistingSnapshot _existing;
        private readonly bool _enableFuzzyMerge;

        public DuplicateResolver(ExistingSnapshot existing, bool enableFuzzyMerge)
        {
            _existing = existing;
            _enableFuzzyMerge = enableFuzzyMerge;
        }

        public ResolutionResult Resolve(IReadOnlyList<ImportCandidate> candidates)
        {
            var result = new ResolutionResult();

            foreach (var candidate in candidates)
            {
                var exactMatches = _existing.FindExactMatches(candidate.Name);
                if (exactMatches.Count == 1)
                {
                    result.ByCandidateKey[candidate.Key] = ResolvedCandidate.Existing(candidate, exactMatches[0], MatchKind.Exact);
                    continue;
                }

                if (exactMatches.Count > 1)
                {
                    result.AmbiguousMatches.Add(new AmbiguousMatch(candidate, exactMatches
                        .Select(x => new CandidateScore(x.TagStableId, x.DisplayName, 1.0m))
                        .ToList()));

                    var chosen = exactMatches.OrderBy(x => x.TagStableId).First();
                    result.ByCandidateKey[candidate.Key] = ResolvedCandidate.Existing(candidate, chosen, MatchKind.Exact);
                    continue;
                }

                if (_enableFuzzyMerge)
                {
                    var fuzzy = _existing.FindFuzzyMatches(candidate.Name);
                    if (fuzzy.Count == 1)
                    {
                        var match = fuzzy[0];
                        result.ByCandidateKey[candidate.Key] = ResolvedCandidate.Existing(candidate, _existing.TagsByStableId[match.TagStableId], MatchKind.Fuzzy);
                        continue;
                    }

                    if (fuzzy.Count > 1)
                    {
                        result.AmbiguousMatches.Add(new AmbiguousMatch(candidate, fuzzy));
                        var chosen = _existing.TagsByStableId[fuzzy[0].TagStableId];
                        result.ByCandidateKey[candidate.Key] = ResolvedCandidate.Existing(candidate, chosen, MatchKind.Fuzzy);
                        continue;
                    }
                }

                result.ByCandidateKey[candidate.Key] = ResolvedCandidate.New(candidate, Guid.NewGuid());
            }

            return result;
        }
    }

    private sealed class ExistingSnapshot
    {
        public required Dictionary<Guid, ExistingTagInfo> TagsByStableId { get; init; }
        public required Dictionary<string, HashSet<Guid>> ExactNameIndex { get; init; }

        public static async Task<ExistingSnapshot> LoadAsync(string connectionString)
        {
            return await WithSqlConnectionAsync(connectionString, async connection =>
            {
                var tags = new Dictionary<Guid, ExistingTagInfo>();

                const string baseSql = """
                    SELECT
                        t.[Id] AS TagVersionId,
                        t.[StableId] AS TagStableId,
                        t.[Name] AS TagName,
                        t.[Description] AS TagDescription,
                        tt.[TypeId],
                        rep.[NodeId] AS RepresentativeNodeVersionId,
                        n.[StableId] AS RepresentativeNodeStableId,
                        n.[Name] AS RepresentativeNodeName,
                        n.[Description] AS RepresentativeNodeDescription
                    FROM [KnowledgeGraph].[Tags] t
                    LEFT JOIN [KnowledgeGraph].[TagTypes] tt ON tt.[TagStableId] = t.[StableId]
                    LEFT JOIN [KnowledgeGraph].[TagRepresentativeNode] rep ON rep.[TagId] = t.[Id]
                    LEFT JOIN [KnowledgeGraph].[KnowledgeNodes] n ON n.[Id] = rep.[NodeId]
                    WHERE t.[IsCurrent] = 1 AND t.[Status] = 'Current';
                    """;

                await using (var cmd = new SqlCommand(baseSql, connection))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var tagVersionId = reader.GetGuid(reader.GetOrdinal("TagVersionId"));
                        var tagStableId = reader.GetGuid(reader.GetOrdinal("TagStableId"));
                        tags[tagStableId] = new ExistingTagInfo
                        {
                            TagVersionId = tagVersionId,
                            TagStableId = tagStableId,
                            BaseName = reader["TagName"] as string,
                            BaseDescription = reader["TagDescription"] as string,
                            TagTypeId = GetNullableInt32(reader, "TypeId"),
                            RepresentativeNodeVersionId = GetNullableGuid(reader, "RepresentativeNodeVersionId"),
                            RepresentativeNodeStableId = GetNullableGuid(reader, "RepresentativeNodeStableId"),
                            RepresentativeNodeBaseName = reader["RepresentativeNodeName"] as string,
                            RepresentativeNodeBaseDescription = reader["RepresentativeNodeDescription"] as string
                        };
                    }
                }

                const string tagL10nSql = """
                    SELECT
                        t.[StableId] AS TagStableId,
                        i.[FieldKey],
                        i.[LangCode],
                        COALESCE(i.[Content], i.[Text]) AS [Value]
                    FROM [KnowledgeGraph].[Tags] t
                    JOIN [L10n].[TagL10nSets] tls ON tls.[TagId] = t.[Id] AND tls.[Relation] = 0
                    JOIN [L10n].[L10nSetItems] si ON si.[L10nSetId] = tls.[L10nSetId]
                    JOIN [L10n].[L10nItems] i ON i.[L10nItemId] = si.[L10nItemId]
                    WHERE t.[IsCurrent] = 1 AND t.[Status] = 'Current'
                      AND i.[FieldKey] IN ('name', 'description');
                    """;

                await using (var cmd = new SqlCommand(tagL10nSql, connection))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var stableId = reader.GetGuid(reader.GetOrdinal("TagStableId"));
                        if (!tags.TryGetValue(stableId, out var tag))
                        {
                            continue;
                        }

                        var fieldKey = reader["FieldKey"] as string;
                        var langCode = reader["LangCode"] as string;
                        var value = reader["Value"] as string;
                        if (!string.IsNullOrWhiteSpace(fieldKey) && !string.IsNullOrWhiteSpace(langCode) && !string.IsNullOrWhiteSpace(value))
                        {
                            tag.TagL10n[(fieldKey, langCode)] = value;
                        }
                    }
                }

                const string nodeL10nSql = """
                    SELECT
                        repTag.[StableId] AS TagStableId,
                        i.[FieldKey],
                        i.[LangCode],
                        COALESCE(i.[Content], i.[Text]) AS [Value]
                    FROM [KnowledgeGraph].[TagRepresentativeNode] rep
                    JOIN [KnowledgeGraph].[Tags] repTag ON repTag.[Id] = rep.[TagId]
                    JOIN [KnowledgeGraph].[KnowledgeNodes] n ON n.[Id] = rep.[NodeId]
                    JOIN [L10n].[NodeL10nSets] nls ON nls.[NodeId] = n.[Id] AND nls.[Relation] = 0
                    JOIN [L10n].[L10nSetItems] si ON si.[L10nSetId] = nls.[L10nSetId]
                    JOIN [L10n].[L10nItems] i ON i.[L10nItemId] = si.[L10nItemId]
                    WHERE repTag.[IsCurrent] = 1 AND repTag.[Status] = 'Current'
                      AND i.[FieldKey] IN ('name', 'description');
                    """;

                await using (var cmd = new SqlCommand(nodeL10nSql, connection))
                await using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var stableId = reader.GetGuid(reader.GetOrdinal("TagStableId"));
                        if (!tags.TryGetValue(stableId, out var tag))
                        {
                            continue;
                        }

                        var fieldKey = reader["FieldKey"] as string;
                        var langCode = reader["LangCode"] as string;
                        var value = reader["Value"] as string;
                        if (!string.IsNullOrWhiteSpace(fieldKey) && !string.IsNullOrWhiteSpace(langCode) && !string.IsNullOrWhiteSpace(value))
                        {
                            tag.RepresentativeNodeL10n[(fieldKey, langCode)] = value;
                        }
                    }
                }

                var exactIndex = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);
                foreach (var tag in tags.Values)
                {
                    foreach (var name in tag.AllNames())
                    {
                        var normalized = NormalizeName(name);
                        if (string.IsNullOrWhiteSpace(normalized))
                        {
                            continue;
                        }

                        if (!exactIndex.TryGetValue(normalized, out var set))
                        {
                            set = new HashSet<Guid>();
                            exactIndex[normalized] = set;
                        }

                        set.Add(tag.TagStableId);
                    }
                }

                return new ExistingSnapshot
                {
                    TagsByStableId = tags,
                    ExactNameIndex = exactIndex
                };
            });
        }

        public List<ExistingTagInfo> FindExactMatches(string importName)
        {
            var normalized = NormalizeName(importName);
            if (!ExactNameIndex.TryGetValue(normalized, out var matches))
            {
                return new List<ExistingTagInfo>();
            }

            return matches
                .Select(id => TagsByStableId[id])
                .OrderBy(x => x.TagStableId)
                .ToList();
        }

        public List<CandidateScore> FindFuzzyMatches(string importName)
        {
            var results = new List<CandidateScore>();
            foreach (var tag in TagsByStableId.Values)
            {
                var bestScore = 0m;
                foreach (var existingName in tag.AllNames())
                {
                    var score = ComputeFuzzyScore(importName, existingName);
                    if (score > bestScore)
                    {
                        bestScore = score;
                    }
                }

                if (bestScore >= 0.95m)
                {
                    results.Add(new CandidateScore(tag.TagStableId, tag.DisplayName, bestScore));
                }
            }

            return results
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToList();
        }

        private static decimal ComputeFuzzyScore(string left, string right)
        {
            var leftNormalized = NormalizeName(left);
            var rightNormalized = NormalizeName(right);
            if (string.IsNullOrWhiteSpace(leftNormalized) || string.IsNullOrWhiteSpace(rightNormalized))
            {
                return 0m;
            }

            if (leftNormalized == rightNormalized)
            {
                return 1m;
            }

            var leftSuffix = StripFuzzySuffix(left);
            var rightSuffix = StripFuzzySuffix(right);
            if (!string.IsNullOrWhiteSpace(leftSuffix) && leftSuffix == rightSuffix)
            {
                return 0.97m;
            }

            if (rightNormalized.StartsWith(leftNormalized, StringComparison.Ordinal) && rightNormalized.Length - leftNormalized.Length == 1)
            {
                return 0.95m;
            }

            if (leftNormalized.StartsWith(rightNormalized, StringComparison.Ordinal) && leftNormalized.Length - rightNormalized.Length == 1)
            {
                return 0.95m;
            }

            return 0m;
        }
    }

    private sealed class ExistingTagInfo
    {
        public Guid TagVersionId { get; init; }
        public Guid TagStableId { get; init; }
        public string? BaseName { get; init; }
        public string? BaseDescription { get; init; }
        public int? TagTypeId { get; init; }
        public Guid? RepresentativeNodeVersionId { get; init; }
        public Guid? RepresentativeNodeStableId { get; init; }
        public string? RepresentativeNodeBaseName { get; init; }
        public string? RepresentativeNodeBaseDescription { get; init; }
        public Dictionary<(string FieldKey, string LangCode), string> TagL10n { get; } = new();
        public Dictionary<(string FieldKey, string LangCode), string> RepresentativeNodeL10n { get; } = new();

        public string DisplayName
            => TagL10n.TryGetValue(("name", "zh"), out var zhName)
                ? zhName
                : BaseName ?? TagStableId.ToString("D");

        public IEnumerable<string> AllNames()
        {
            if (!string.IsNullOrWhiteSpace(BaseName))
            {
                yield return BaseName;
            }

            foreach (var name in TagL10n.Where(x => x.Key.FieldKey == "name").Select(x => x.Value))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }

            if (!string.IsNullOrWhiteSpace(RepresentativeNodeBaseName))
            {
                yield return RepresentativeNodeBaseName;
            }

            foreach (var name in RepresentativeNodeL10n.Where(x => x.Key.FieldKey == "name").Select(x => x.Value))
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    yield return name;
                }
            }
        }
    }

    private sealed class ImportOptions
    {
        public required string InputPath { get; init; }
        public required string SettingsPath { get; init; }
        public required string PreviewPath { get; init; }
        public required string SystemUser { get; init; }
        public bool Apply { get; init; }
        public bool Neo4jOnly { get; init; }
        public bool EnableFuzzyMerge { get; init; }
        public bool AllowAmbiguousMatches { get; init; }

        public static ImportOptions Parse(string[] args, string workspaceRoot)
        {
            var arguments = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < args.Length; i++)
            {
                var current = args[i];
                if (!current.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    arguments[current] = args[++i];
                }
                else
                {
                    arguments[current] = null;
                }
            }

            var defaultSettings = Path.Combine(workspaceRoot, "SciencetopiaWebApplication", "appsettings.json");
            if (!File.Exists(defaultSettings))
            {
                defaultSettings = Path.Combine(workspaceRoot, "SciencetopiaWebApplication", "appsettings.Development.json");
            }

            var defaultInput = Path.GetFullPath(Path.Combine(workspaceRoot, "..", "all_150.json"));
            var defaultPreview = Path.Combine(workspaceRoot, "temp", "all150_import_preview.json");

            return new ImportOptions
            {
                InputPath = Path.GetFullPath(arguments.TryGetValue("--input", out var input) && !string.IsNullOrWhiteSpace(input) ? input : defaultInput),
                SettingsPath = Path.GetFullPath(arguments.TryGetValue("--settings", out var settings) && !string.IsNullOrWhiteSpace(settings) ? settings : defaultSettings),
                PreviewPath = Path.GetFullPath(arguments.TryGetValue("--preview", out var preview) && !string.IsNullOrWhiteSpace(preview) ? preview : defaultPreview),
                SystemUser = arguments.TryGetValue("--system-user", out var systemUser) && !string.IsNullOrWhiteSpace(systemUser)
                    ? systemUser
                    : "system:all150-import",
                Apply = arguments.ContainsKey("--apply"),
                Neo4jOnly = arguments.ContainsKey("--neo4j-only"),
                EnableFuzzyMerge = !arguments.ContainsKey("--disable-fuzzy-merge"),
                AllowAmbiguousMatches = arguments.ContainsKey("--allow-ambiguous-matches")
            };
        }
    }

    private sealed class ImportManifest
    {
        public required Dictionary<string, ImportCandidate> Candidates { get; init; }
        public required HashSet<ManifestEdge> Edges { get; init; }
    }

    private sealed class ImportCandidate
    {
        public required string Key { get; init; }
        public required string Name { get; set; }
        public required string Level { get; set; }
        public string? Description { get; set; }
    }

    private sealed class ResolutionResult
    {
        public Dictionary<string, ResolvedCandidate> ByCandidateKey { get; } = new(StringComparer.Ordinal);
        public List<AmbiguousMatch> AmbiguousMatches { get; } = new();
    }

    private sealed class ResolvedCandidate
    {
        public required ImportCandidate Candidate { get; init; }
        public required MatchKind MatchKind { get; init; }
        public required Guid ResolvedTagStableId { get; init; }
        public Guid? TagVersionId { get; set; }
        public Guid? RepresentativeNodeVersionId { get; set; }
        public Guid? RepresentativeNodeStableId { get; set; }

        public static ResolvedCandidate Existing(ImportCandidate candidate, ExistingTagInfo existing, MatchKind matchKind)
            => new()
            {
                Candidate = candidate,
                MatchKind = matchKind,
                ResolvedTagStableId = existing.TagStableId,
                TagVersionId = existing.TagVersionId,
                RepresentativeNodeVersionId = existing.RepresentativeNodeVersionId,
                RepresentativeNodeStableId = existing.RepresentativeNodeStableId
            };

        public static ResolvedCandidate New(ImportCandidate candidate, Guid stableId)
            => new()
            {
                Candidate = candidate,
                MatchKind = MatchKind.New,
                ResolvedTagStableId = stableId
            };
    }

    private enum MatchKind
    {
        Exact,
        Fuzzy,
        New
    }

    private enum EntityScope
    {
        Tag,
        Node
    }

    private sealed class AppSettings
    {
        public required ConnectionStrings ConnectionStrings { get; init; }
        public required Neo4jSettings Neo4j { get; init; }
    }

    private sealed class ConnectionStrings
    {
        public required string DefaultConnection { get; init; }
    }

    private sealed class Neo4jSettings
    {
        public required string Uri { get; init; }
        public required string User { get; init; }
        public required string Password { get; init; }
    }

    private sealed class InputRow
    {
        public string? Discipline { get; init; }
        public string? Subject { get; init; }
        public string? Field { get; init; }
        public string? Topic { get; init; }
        public Dictionary<string, string>? DataDetail { get; init; }
    }

    private sealed record LevelSeed(string Level, string? Name, string? Description);
    private sealed record ManifestEdge(string ParentKey, string ChildKey);
    private sealed record ResolvedEdge(Guid ParentStableId, Guid ChildStableId);
    private sealed record CandidateScore(Guid TagStableId, string Name, decimal Score);
    private sealed record AmbiguousMatch(ImportCandidate Candidate, List<CandidateScore> Candidates);

    private sealed class GraphProjectionRow
    {
        public required string TagStableId { get; init; }
        public required string TagVersionId { get; init; }
        public required string NodeStableId { get; init; }
        public required string NodeVersionId { get; init; }
        public required string Level { get; init; }
    }

    private sealed class PreviewReport
    {
        public int SourceRowCount { get; init; }
        public int UniqueImportTagCount { get; init; }
        public int ExactMatchCount { get; init; }
        public int FuzzyMatchCount { get; init; }
        public int NewTagCount { get; init; }
        public int ContainEdgeCount { get; init; }
        public int ExistingCurrentTagCount { get; init; }
        public required Dictionary<string, int> LevelCounts { get; init; }
        public required List<AmbiguousMatchReport> AmbiguousMatches { get; init; }
        public required List<TagPreviewItem> SampleNewTags { get; init; }
    }

    private sealed class AmbiguousMatchReport
    {
        public required string ImportName { get; init; }
        public required string Level { get; init; }
        public required List<MatchCandidateReport> Candidates { get; init; }
    }

    private sealed class MatchCandidateReport
    {
        public required string Name { get; init; }
        public decimal Score { get; init; }
    }

    private sealed class TagPreviewItem
    {
        public required string Name { get; init; }
        public required string Level { get; init; }
        public string? Description { get; init; }
    }

    private sealed class ApplyReport
    {
        public int CreatedTagCount { get; set; }
        public int ReusedTagCount { get; set; }
        public int CreatedRepresentativeNodeCount { get; set; }
        public int ProjectedTagCount { get; set; }
        public int ContainEdgeCount { get; set; }
    }
}
