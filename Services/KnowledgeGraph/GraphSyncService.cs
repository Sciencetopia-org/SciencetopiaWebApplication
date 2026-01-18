using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;

namespace Sciencetopia.Services.KnowledgeGraph;

public interface IGraphSyncService
{
    Task<int> SyncLatestNodesAsync(CancellationToken ct = default);
    Task<int> SyncLatestTagsAsync(CancellationToken ct = default);
    Task<(int nodes, int tags)> SyncAllAsync(CancellationToken ct = default);
}

public sealed class GraphSyncService : IGraphSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly IDriver _driver;

    public GraphSyncService(ApplicationDbContext db, IDriver driver)
    {
        _db = db;
        _driver = driver;
    }

    public async Task<(int nodes, int tags)> SyncAllAsync(CancellationToken ct = default)
    {
        var n = await SyncLatestNodesAsync(ct);
        var t = await SyncLatestTagsAsync(ct);
        return (n, t);
    }

    public async Task<int> SyncLatestNodesAsync(CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQueryRaw<StableRow>(
            "SELECT Id, StableId FROM KnowledgeGraph.vLatestKnowledgeNodes").ToListAsync(ct);

        var count = 0;
        await using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
        await session.ExecuteWriteAsync(async tx =>
        {
            foreach (var r in rows)
            {
                var cy = @"MERGE (n:KnowledgeNode { stableId: $sid })\nSET n.stableId = $sid, n.currentVersionId = $vid";
                await tx.RunAsync(cy, new { sid = r.StableId.ToString(), vid = r.Id.ToString() });
                count++;
            }
        });
        return count;
    }

    public async Task<int> SyncLatestTagsAsync(CancellationToken ct = default)
    {
        var rows = await _db.Database.SqlQueryRaw<StableRow>(
            "SELECT Id, StableId FROM KnowledgeGraph.vLatestTags").ToListAsync(ct);

        var count = 0;
        await using var session = _driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Write));
        await session.ExecuteWriteAsync(async tx =>
        {
            foreach (var r in rows)
            {
                var cy = @"MERGE (t:Tag { stableId: $sid })\nSET t.stableId = $sid, t.currentVersionId = $vid";
                await tx.RunAsync(cy, new { sid = r.StableId.ToString(), vid = r.Id.ToString() });
                count++;
            }
        });
        return count;
    }

    private sealed class StableRow
    {
        public Guid Id { get; set; }
        public Guid StableId { get; set; }
    }
}

