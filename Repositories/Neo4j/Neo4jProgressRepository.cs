using Neo4j.Driver;
using Sciencetopia.Constants;
using Sciencetopia.DTOs;

namespace Sciencetopia.Repositories.Neo4j;

public class Neo4jProgressRepository : INeo4jProgressRepository
{
    private readonly IDriver _driver;

    public Neo4jProgressRepository(IDriver driver)
    {
        _driver = driver;
    }

    public async Task CompleteResourceAsync(string userId, Guid resourceId, DateTime completedAt, string? source, string? device, int? spentSeconds)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
MATCH (r:Resource {id:$resourceId})
MERGE (u)-[c:COMPLETED]->(r)
SET c.completedAt = coalesce(c.completedAt, $completedAt),
    c.source = $source, c.device = $device,
    c.spentSeconds = coalesce(c.spentSeconds,0) + coalesce($spentSeconds,0)
";
            await tx.RunAsync(cypher, new
            {
                userId,
                resourceId = resourceId.ToString(),
                completedAt,
                source,
                device,
                spentSeconds
            });
        });
    }

    public async Task UndoCompleteResourceAsync(string userId, Guid resourceId)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})-[c:COMPLETED]->(r:Resource {id:$resourceId})
DELETE c
";
            await tx.RunAsync(cypher, new { userId, resourceId = resourceId.ToString() });
        });
    }

    public async Task<UserPlanProgressDto> GetMyPlanProgressAsync(string userId, Guid planId)
    {
        await using var session = _driver.AsyncSession();
        var (completed, total) = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})-[:HAS_STEP]->(:Lesson)-[:HAS_RESOURCE]->(r:Resource)
WITH COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (u:User {id:$userId})-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
RETURN size(total) AS totalResources, size(COLLECT(DISTINCT rc)) AS completed
";
            var cursor = await tx.RunAsync(cypher, new { planId = planId.ToString(), userId });
            var rec = await cursor.SingleAsync();
            return (rec["completed"].As<int>(), rec["totalResources"].As<int>());
        });
        var progress = total == 0 ? 0.0 : ((double)completed / total) * 100.0;
        // Advanced-only progress
        var (advCompleted, advTotal) = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})-[hs:HAS_STEP]->(l:Lesson)
WHERE hs.type IN $advancedTypes
OPTIONAL MATCH (l)-[:HAS_RESOURCE]->(r:Resource)
WITH COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (u:User {id:$userId})-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
RETURN size(total) AS totalResources, size(COLLECT(DISTINCT rc)) AS completed
";
            var cursor = await tx.RunAsync(cypher, new { planId = planId.ToString(), userId, advancedTypes = StudyPlanStepTypes.AdvancedTopicAliases });
            var rec = await cursor.SingleAsync();
            return (rec["completed"].As<int>(), rec["totalResources"].As<int>());
        });
        var advProgress = advTotal == 0 ? 0.0 : ((double)advCompleted / advTotal) * 100.0;
        return new UserPlanProgressDto(progress, Enumerable.Empty<UserLessonProgressDto>(), advProgress);
    }

    public async Task<UserLessonProgressDto> GetMyLessonProgressAsync(string userId, Guid lessonId)
    {
        await using var session = _driver.AsyncSession();
        var (completed, total) = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (l:Lesson {id:$lessonId})-[:HAS_RESOURCE]->(r:Resource)
WITH COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (u:User {id:$userId})-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
RETURN size(total) AS totalResources, size(COLLECT(DISTINCT rc)) AS completed
";
            var cursor = await tx.RunAsync(cypher, new { lessonId = lessonId.ToString(), userId });
            var rec = await cursor.SingleAsync();
            return (rec["completed"].As<int>(), rec["totalResources"].As<int>());
        });
        var progress = total == 0 ? 0.0 : (double)completed / total;
        return new UserLessonProgressDto(lessonId, progress, completed, total);
    }

    public async Task<UserPlanProgressDto> GetMyPlanProgressWithLessonsAsync(string userId, Guid planId)
    {
        await using var session = _driver.AsyncSession();

        // Overall plan progress
        var overall = await GetMyPlanProgressAsync(userId, planId);

        // Per-lesson progress
        var perLesson = await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (p:StudyPlan {id:$planId})-[:HAS_STEP]->(l:Lesson)
OPTIONAL MATCH (l)-[:HAS_RESOURCE]->(r:Resource)
WITH l, COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (u:User {id:$userId})-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
WITH l, size(total) AS totalResources, size(COLLECT(DISTINCT rc)) AS completed
RETURN l.id AS lessonId, completed AS completed, totalResources AS totalResources
";
            var cursor = await tx.RunAsync(cypher, new { planId = planId.ToString(), userId });
            var list = new List<UserLessonProgressDto>();
            await cursor.ForEachAsync(r =>
            {
                var lidStr = r["lessonId"].As<string>();
                Guid.TryParse(lidStr, out var lid);
                var completed = r["completed"].As<int>();
                var total = r["totalResources"].As<int>();
                var progress = total == 0 ? 0.0 : (double)completed / total;
                list.Add(new UserLessonProgressDto(lid, progress, completed, total));
            });
            return list;
        });

        return new UserPlanProgressDto(overall.planProgress, perLesson, overall.advancedTopicProgress);
    }

    public async Task<CohortSummaryDto> GetCohortSummaryAsync(Guid cohortId)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
OPTIONAL MATCH (pv)-[:HAS_RESOURCE]->(r:Resource)
WITH c, pv, COLLECT(DISTINCT r) AS total
// Cohort membership
OPTIONAL MATCH (c)<-[:IN_COHORT]-(u:User)
WITH pv, total, collect(u) AS members
// Filter users whose enrollment aligns to cohort's version
UNWIND members AS u
OPTIONAL MATCH (u)-[:ENROLLED_IN]->(v:PlanVersion {studyPlanId: pv.studyPlanId})
WITH pv, total, u, v
WITH total,
     collect(CASE WHEN v.versionNumber = pv.versionNumber THEN u ELSE null END) AS aligned,
     collect(CASE WHEN v.versionNumber <> pv.versionNumber OR v IS NULL THEN u ELSE null END) AS mismatched
WITH total,
     [x IN aligned WHERE x IS NOT NULL] AS alignedUsers,
     [x IN mismatched WHERE x IS NOT NULL] AS mismatchUsers
UNWIND alignedUsers AS au
OPTIONAL MATCH (au)-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
WITH size(total) AS totalResources, alignedUsers, mismatchUsers, au, COUNT(DISTINCT rc) AS completed
WITH totalResources, alignedUsers, mismatchUsers, COLLECT(completed) AS comp
WITH totalResources, size(alignedUsers) AS memberCount, size(mismatchUsers) AS mismatchCount, comp
RETURN CASE WHEN memberCount=0 OR totalResources=0 THEN 0.0
            ELSE reduce(s=0.0, x IN comp | s + (toFloat(x)/toFloat(totalResources))) / toFloat(memberCount)
       END AS avgProgress,
       memberCount AS memberCount,
       mismatchCount AS versionMismatchCount
";
            var cursor = await tx.RunAsync(cypher, new { cohortId = cohortId.ToString() });
            var rec = await cursor.SingleAsync();
            var avg = rec?["avgProgress"].As<double?>() ?? 0.0;
            var members = rec?["memberCount"].As<int?>() ?? 0;
            return new CohortSummaryDto(avg, members);
        });
    }

    public async Task<List<LessonStatDto>> GetCohortLessonStatsAsync(Guid cohortId)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
OPTIONAL MATCH (pv)-[:HAS_STEP]->(l:Lesson)
OPTIONAL MATCH (pv)-[:HAS_RESOURCE {lessonId: l.id}]->(r:Resource)
WITH c, pv, l, COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (c)<-[:IN_COHORT]-(u:User)
WITH pv, l, total, collect(u) AS members
UNWIND members AS u
OPTIONAL MATCH (u)-[:ENROLLED_IN]->(v:PlanVersion {studyPlanId: pv.studyPlanId})
WITH pv, l, total, u, v
WITH l, total, collect(CASE WHEN v.versionNumber = pv.versionNumber THEN u ELSE null END) AS aligned
WITH l, total, [x IN aligned WHERE x IS NOT NULL] AS alignedUsers
UNWIND alignedUsers AS au
OPTIONAL MATCH (au)-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
WITH l, total, au, COUNT(DISTINCT rc) AS completed
WITH l, total, COLLECT(completed) AS perUserCounts, COUNT(DISTINCT au) AS members
RETURN l.id AS lessonId, coalesce(l.title, '') AS lessonTitle,
       CASE WHEN size(total)=0 OR members=0 THEN 0.0 ELSE reduce(s=0.0, x IN perUserCounts | s + (toFloat(x)/toFloat(size(total)))) / toFloat(members) END AS lessonAvgProgress,
       reduce(s=0, x IN perUserCounts | s + x) AS completedCount,
       size(total) AS totalResources
";
            var cursor = await tx.RunAsync(cypher, new { cohortId = cohortId.ToString() });
            var list = new List<LessonStatDto>();
            await cursor.ForEachAsync(r =>
            {
                var lidStr = r["lessonId"].As<string>();
                Guid.TryParse(lidStr, out var lid);
                var title = r["lessonTitle"].As<string?>() ?? string.Empty;
                var avg = r["lessonAvgProgress"].As<double?>() ?? 0.0;
                var completed = r["completedCount"].As<int>();
                var total = r["totalResources"].As<int>();
                list.Add(new LessonStatDto(lid, title, avg, completed, total));
            });
            return list;
        });
    }

    public async Task<List<LeaderboardItemDto>> GetCohortLeaderboardAsync(Guid cohortId, int top)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (c:Cohort {id:$cohortId})-[:OF_VERSION]->(pv:PlanVersion)
OPTIONAL MATCH (pv)-[:HAS_RESOURCE]->(r:Resource)
WITH c, pv, COLLECT(DISTINCT r) AS total
OPTIONAL MATCH (c)<-[:IN_COHORT]-(u:User)
WITH pv, total, collect(u) AS members
UNWIND members AS u
OPTIONAL MATCH (u)-[:ENROLLED_IN]->(v:PlanVersion {studyPlanId: pv.studyPlanId})
WITH pv, total, u, v
WITH total, u
WHERE v.versionNumber = pv.versionNumber
OPTIONAL MATCH (u)-[:COMPLETED]->(rc:Resource)
WHERE rc IN total
WITH u, size(total) AS totalResources, COUNT(DISTINCT rc) AS completed
WITH u, CASE WHEN totalResources=0 THEN 0.0 ELSE toFloat(completed)/toFloat(totalResources) END AS progress
RETURN u.id AS userId, coalesce(u.displayName, u.id) AS displayName, progress
ORDER BY progress DESC
LIMIT $top
";
            var cursor = await tx.RunAsync(cypher, new { cohortId = cohortId.ToString(), top });
            var list = new List<LeaderboardItemDto>();
            await cursor.ForEachAsync(r =>
            {
                list.Add(new LeaderboardItemDto(
                    r["userId"].As<string>(),
                    r["displayName"].As<string?>() ?? string.Empty,
                    r["progress"].As<double?>() ?? 0.0
                ));
            });
            return list;
        });
    }

    public async Task<List<Guid>> GetCohortsForUserAndPlanAsync(string userId, Guid planId)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})-[:ENROLLED_IN]->(pv:PlanVersion {studyPlanId:$planId})
MATCH (c:Cohort)-[:OF_VERSION]->(pv)
RETURN c.id AS id
";
            var cursor = await tx.RunAsync(cypher, new { userId, planId = planId.ToString() });
            var list = new List<Guid>();
            await cursor.ForEachAsync(r =>
            {
                var s = r["id"].As<string>();
                if (Guid.TryParse(s, out var gid)) list.Add(gid);
            });
            return list;
        });
    }

    public async Task<bool> ToggleCompleteByLinkAsync(string userId, string resourceLink, DateTime now, string? source, string? device)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            var cypher = @"
MATCH (u:User {id:$userId})
MATCH (r:Resource {link:$link})
OPTIONAL MATCH (u)-[c:COMPLETED]->(r)
WITH u, r, c
FOREACH (_ IN CASE WHEN c IS NULL THEN [1] ELSE [] END |
  MERGE (u)-[nc:COMPLETED]->(r)
  SET nc.completedAt = coalesce(nc.completedAt, $now),
      nc.source = $source,
      nc.device = $device
)
FOREACH (_ IN CASE WHEN c IS NOT NULL THEN [1] ELSE [] END |
  DELETE c
)
RETURN c IS NULL AS completedNow
";
            var cursor = await tx.RunAsync(cypher, new { userId, link = resourceLink, now, source, device });
            var rec = await cursor.SingleAsync();
            return rec["completedNow"].As<bool>();
        });
    }

    public async Task<HashSet<Guid>> GetCompletedResourceIdsAsync(string userId, IEnumerable<Guid> resourceIds)
    {
        var ids = resourceIds?.ToArray() ?? Array.Empty<Guid>();
        if (ids.Length == 0) return new HashSet<Guid>();
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            var cypher = @"
UNWIND $ids AS rid
MATCH (r:Resource {id: rid})
WITH COLLECT(r) AS target
MATCH (u:User {id:$userId})-[:COMPLETED]->(rc:Resource)
WHERE rc IN target
RETURN COLLECT(DISTINCT rc.id) AS completedIds
";
            var idsStr = ids.Select(x => x.ToString()).ToArray();
            var cursor = await tx.RunAsync(cypher, new { userId, ids = idsStr });
            var rec = await cursor.SingleAsync();
            var arr = rec["completedIds"].As<List<object>>();
            var set = new HashSet<Guid>();
            foreach (var o in arr)
            {
                var s = o?.ToString();
                if (Guid.TryParse(s, out var gid)) set.Add(gid);
            }
            return set;
        });
    }
}
