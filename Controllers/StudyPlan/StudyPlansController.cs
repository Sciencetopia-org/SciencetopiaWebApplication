using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models;
using Sciencetopia.Models.Enums;
using Sciencetopia.Services;
using Neo4j.Driver;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using System.Data;

namespace Sciencetopia.Controllers.StudyPlan
{
    [ApiController]
    [Route("api/StudyPlans")] // PascalCase
    public class StudyPlansController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PermissionService _perm;
        private readonly Neo4j.Driver.IDriver _driver;

        public StudyPlansController(ApplicationDbContext db, PermissionService perm, Neo4j.Driver.IDriver driver)
        {
            _db = db;
            _perm = perm;
            _driver = driver;
        }

        private static bool IsMissingObjectException(Exception ex, params string[] objectNames)
        {
            if (ex is not SqlException sqlEx || sqlEx.Number != 208) return false;
            if (objectNames == null || objectNames.Length == 0) return true;
            return objectNames.Any(n => sqlEx.Message.Contains(n, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<bool> TableExistsAsync(string schema, string tableName)
        {
            var conn = _db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            try
            {
                if (shouldClose) await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT CASE WHEN OBJECT_ID(@obj, N'U') IS NULL THEN 0 ELSE 1 END";

                var p = cmd.CreateParameter();
                p.ParameterName = "@obj";
                p.Value = $"[{schema}].[{tableName}]";
                cmd.Parameters.Add(p);

                var scalar = await cmd.ExecuteScalarAsync();
                if (scalar == null || scalar == DBNull.Value) return false;
                return Convert.ToInt32(scalar) == 1;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        private async Task<List<Guid>> QueryStableIdsFromLegacyEnrollmentTableAsync(string tableName, string userId)
        {
            var result = new List<Guid>();
            var conn = _db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            try
            {
                if (shouldClose) await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT DISTINCT [StudyPlanStableId]
FROM [StudyPlans].[{tableName}]
WHERE [UserId] = @uid
  AND ([Status] IS NULL OR [Status] IN (N'Active', N'active', N'Learning', N'learning', N'Completed', N'completed'))
  AND [StudyPlanStableId] IS NOT NULL";

                var p = cmd.CreateParameter();
                p.ParameterName = "@uid";
                p.Value = userId;
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var sidObj = reader["StudyPlanStableId"];
                    if (sidObj == DBNull.Value) continue;
                    if (Guid.TryParse(sidObj.ToString(), out var sid)) result.Add(sid);
                }
            }
            catch
            {
                return new List<Guid>();
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
            return result.Distinct().ToList();
        }

        private async Task<bool> HasActiveEnrollmentInLegacyTableAsync(string tableName, string userId, Guid stableId)
        {
            var conn = _db.Database.GetDbConnection();
            var shouldClose = conn.State != ConnectionState.Open;
            try
            {
                if (shouldClose) await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
SELECT TOP 1 1
FROM [StudyPlans].[{tableName}]
WHERE [UserId] = @uid
  AND [StudyPlanStableId] = @sid
  AND ([Status] IS NULL OR [Status] IN (N'Active', N'active', N'Learning', N'learning', N'Completed', N'completed'))";

                var p1 = cmd.CreateParameter();
                p1.ParameterName = "@uid";
                p1.Value = userId;
                cmd.Parameters.Add(p1);

                var p2 = cmd.CreateParameter();
                p2.ParameterName = "@sid";
                p2.Value = stableId;
                cmd.Parameters.Add(p2);

                var scalar = await cmd.ExecuteScalarAsync();
                return scalar != null && scalar != DBNull.Value;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (shouldClose) await conn.CloseAsync();
            }
        }

        private async Task<List<Guid>> GetDirectRoleStableIdsCompatAsync(string userId)
        {
            try
            {
                return await _db.StudyPlanUserRoles.AsNoTracking()
                    .Where(r => r.UserId == userId)
                    .Select(r => r.PlanStableId)
                    .Distinct()
                    .ToListAsync();
            }
            catch (Exception ex) when (IsMissingObjectException(ex, "StudyPlanUserRoles"))
            {
                return new List<Guid>();
            }
        }

        private async Task<List<Guid>> GetGroupSharedStableIdsCompatAsync(List<Guid> groupIds)
        {
            if (groupIds == null || groupIds.Count == 0) return new List<Guid>();
            try
            {
                return await _db.StudyGroupStudyPlans.AsNoTracking()
                    .Where(gp => groupIds.Contains(gp.StudyGroupId))
                    .Select(gp => gp.StudyPlanStableId)
                    .Distinct()
                    .ToListAsync();
            }
            catch (Exception ex) when (IsMissingObjectException(ex, "StudyGroupStudyPlans"))
            {
                return new List<Guid>();
            }
        }

        private async Task<List<Guid>> GetEnrolledStableIdsCompatAsync(string userId)
        {
            var result = new HashSet<Guid>();

            if (await TableExistsAsync("StudyPlans", "UserStudyPlanEnrollments"))
            {
                var rows = await QueryStableIdsFromLegacyEnrollmentTableAsync("UserStudyPlanEnrollments", userId);
                result.UnionWith(rows.Where(x => x != Guid.Empty));
            }

            if (await TableExistsAsync("StudyPlans", "_legacy_UserStudyPlanEnrollments"))
            {
                var rows = await QueryStableIdsFromLegacyEnrollmentTableAsync("_legacy_UserStudyPlanEnrollments", userId);
                result.UnionWith(rows.Where(x => x != Guid.Empty));
            }

            if (await TableExistsAsync("StudyPlans", "StudyPlanEnrollments"))
            {
                try
                {
                    var rows = await _db.StudyPlanEnrollments.AsNoTracking()
                        .Where(e => e.UserId == userId
                            && (string.IsNullOrEmpty(e.Status)
                                || e.Status == "Active" || e.Status == "active"
                                || e.Status == "Learning" || e.Status == "learning"
                                || e.Status == "Completed" || e.Status == "completed"))
                        .Join(_db.StudyPlans.AsNoTracking(),
                            e => e.PlanVersionId,
                            p => p.Id,
                            (e, p) => p.StableId == Guid.Empty ? p.Id : p.StableId)
                        .Distinct()
                        .ToListAsync();
                    result.UnionWith(rows.Where(x => x != Guid.Empty));
                }
                catch (Exception ex) when (IsMissingObjectException(ex, "StudyPlanEnrollments"))
                {
                }
            }

            return result.ToList();
        }

        private async Task<bool> HasActivePlanEnrollmentByStableIdCompatAsync(string userId, Guid stableId)
        {
            if (await TableExistsAsync("StudyPlans", "UserStudyPlanEnrollments"))
            {
                var hit = await HasActiveEnrollmentInLegacyTableAsync("UserStudyPlanEnrollments", userId, stableId);
                if (hit) return true;
            }

            if (await TableExistsAsync("StudyPlans", "_legacy_UserStudyPlanEnrollments"))
            {
                var hit = await HasActiveEnrollmentInLegacyTableAsync("_legacy_UserStudyPlanEnrollments", userId, stableId);
                if (hit) return true;
            }

            if (await TableExistsAsync("StudyPlans", "StudyPlanEnrollments"))
            {
                try
                {
                    return await _db.StudyPlanEnrollments.AsNoTracking()
                        .Where(x => x.UserId == userId && x.ScopeType == "Cohort" && x.Status == "Active")
                        .Join(_db.StudyPlans.AsNoTracking(),
                            e => e.PlanVersionId,
                            p => p.Id,
                            (e, p) => new
                            {
                                StableId = p.StableId == Guid.Empty ? p.Id : p.StableId
                            })
                        .AnyAsync(x => x.StableId == stableId);
                }
                catch (Exception ex) when (IsMissingObjectException(ex, "StudyPlanEnrollments"))
                {
                    return false;
                }
            }

            return false;
        }

        // User-group membership source-of-truth: Groups.UserGroups
        private async Task<List<Guid>> GetActiveGroupIdsForUserAsync(string userId)
        {
            return await _db.UserGroups.AsNoTracking()
                .Where(gr => gr.UserId == userId && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"))
                .Select(gr => gr.GroupId)
                .Distinct()
                .ToListAsync();
        }

        private async Task<bool> IsGroupMemberAsync(string userId, Guid groupId)
        {
            return await _db.UserGroups.AsNoTracking()
                .AnyAsync(gr => gr.GroupId == groupId
                    && gr.UserId == userId
                    && (string.IsNullOrEmpty(gr.Status) || gr.Status == "Active" || gr.Status == "active"));
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? q = null, [FromQuery] string? sort = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var ug = Guid.TryParse(userId, out var parsed) ? parsed : Guid.Empty;

            // SQL is the source of truth for visibility.
            var stableSetSql = new HashSet<Guid>();

            var createdStableIds = await _db.StudyPlans.AsNoTracking()
                .Where(p => (ug != Guid.Empty && p.CreatorId == ug) || p.CreatedBy == userId)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .Distinct()
                .ToListAsync();
            stableSetSql.UnionWith(createdStableIds.Where(x => x != Guid.Empty));

            var directRoleStableIds = await GetDirectRoleStableIdsCompatAsync(userId);
            stableSetSql.UnionWith(directRoleStableIds.Where(x => x != Guid.Empty));

            var myGroupIds = await GetActiveGroupIdsForUserAsync(userId);
            if (myGroupIds.Count > 0)
            {
                var groupSharedStableIds = await GetGroupSharedStableIdsCompatAsync(myGroupIds);
                stableSetSql.UnionWith(groupSharedStableIds.Where(x => x != Guid.Empty));
            }

            var enrolledStableIds = await GetEnrolledStableIdsCompatAsync(userId);
            stableSetSql.UnionWith(enrolledStableIds.Where(x => x != Guid.Empty));

            // Optional graph compatibility: supplement SQL visibility if graph has legacy links.
            try
            {
                var planIdsFromGraph = new HashSet<Guid>();
                await using var session = _driver.AsyncSession();

                var createdCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:CREATED]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await createdCur.FetchAsync())
                {
                    var idStr = createdCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                var shareCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:MEMBER_OF]->(:StudyGroup)-[:SHARES_PLAN]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await shareCur.FetchAsync())
                {
                    var idStr = shareCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                var enrollPvCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:ENROLLED_IN]->(pv:PlanVersion) RETURN DISTINCT pv.studyPlanId AS id", new { uid = userId });
                while (await enrollPvCur.FetchAsync())
                {
                    var idStr = enrollPvCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                var enrollPlanCur = await session.RunAsync("MATCH (u:User {id:$uid})-[:ENROLLED_IN]->(p:StudyPlan) RETURN DISTINCT p.id AS id", new { uid = userId });
                while (await enrollPlanCur.FetchAsync())
                {
                    var idStr = enrollPlanCur.Current["id"].As<string>();
                    if (!string.IsNullOrWhiteSpace(idStr) && Guid.TryParse(idStr, out var gid)) planIdsFromGraph.Add(gid);
                }

                stableSetSql.UnionWith(planIdsFromGraph.Where(x => x != Guid.Empty));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[StudyPlansController.List] Graph read failed for user {userId}: {ex.Message}");
            }

            var stableSet = stableSetSql.ToList();
            Console.WriteLine($"[StudyPlansController.List] User {userId} has {stableSet.Count} visible plan stableIds from SQL/graph.");

            var baseQ = _db.StudyPlans.AsNoTracking().Where(p =>
                stableSet.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId)
                // public plans (case-insensitive)
                || (EF.Property<string>(p, "Privacy") != null && (EF.Property<string>(p, "Privacy").ToLower() == "public"))
            );

            if (!string.IsNullOrWhiteSpace(q))
            {
                baseQ = baseQ.Where(p => p.Title.Contains(q));
            }

            baseQ = sort switch
            {
                "createdDesc" => baseQ.OrderByDescending(p => p.CreatedDate),
                "createdAsc" => baseQ.OrderBy(p => p.CreatedDate),
                _ => baseQ.OrderByDescending(p => p.UpdatedDate)
            };

            var total = await baseQ.CountAsync();

            // Project only Id and Title for speed
            var slice = await baseQ
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new
                {
                    p.Id,
                    p.StableId,
                    p.VersionNumber,
                    p.IsCurrent,
                    p.Status,
                    p.Title,
                    p.Description,
                    p.CreatedDate,
                    p.UpdatedDate
                })
                .ToListAsync();

            var stableIds = slice.Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId).Distinct().ToList();

            var aggregates = await _db.StudyPlans.AsNoTracking()
                .Where(p => stableIds.Contains(p.StableId == Guid.Empty ? p.Id : p.StableId))
                .GroupBy(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .Select(g => new
                {
                    StableId = g.Key,
                    LatestVersionNumber = g.Max(p => p.VersionNumber),
                    CurrentVersionNumber = g.Where(p => p.IsCurrent).Select(p => (int?)p.VersionNumber).FirstOrDefault()
                })
                .ToListAsync();

            var aggregatesDict = aggregates.ToDictionary(a => a.StableId, a => a);

            var items = await Task.WhenAll(slice.Select(async p =>
            {
                var stableId = p.StableId == Guid.Empty ? p.Id : p.StableId;
                var agg = aggregatesDict.TryGetValue(stableId, out var entry)
                    ? entry
                    : new { StableId = stableId, LatestVersionNumber = p.VersionNumber, CurrentVersionNumber = (int?) (p.IsCurrent ? p.VersionNumber : (int?)null) };

                var currentVersionNumber = agg.CurrentVersionNumber ?? agg.LatestVersionNumber;
                var hasUpgrade = !p.IsCurrent && p.VersionNumber < agg.LatestVersionNumber;

                var role = (await _perm.GetEffectivePlanRoleAsync(userId, p.Id)).ToString();

                return new
                {
                    id = p.Id,
                    stableId,
                    versionNumber = p.VersionNumber,
                    latestVersionNumber = agg.LatestVersionNumber,
                    currentVersionNumber,
                    isCurrent = p.IsCurrent,
                    status = p.Status,
                    title = p.Title,
                    description = p.Description,
                    updatedAt = p.UpdatedDate,
                    createdAt = p.CreatedDate,
                    hasUpgrade,
                    role
                };
            }));

            return Ok(new { total, page, pageSize, items });
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var plan = await _db.StudyPlans.FirstOrDefaultAsync(p => p.Id == id);
            if (plan == null) return NotFound();

            var stableId = plan.StableId == Guid.Empty ? plan.Id : plan.StableId;

            var current = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .OrderByDescending(p => p.IsCurrent)
                .ThenByDescending(p => p.VersionNumber)
                .Select(p => new { p.Id, p.VersionNumber, p.IsCurrent })
                .FirstOrDefaultAsync();

            var latestVersionNumber = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.StableId == stableId)
                .MaxAsync(p => (int?)p.VersionNumber) ?? plan.VersionNumber;

            return Ok(new
            {
                id = plan.Id,
                stableId,
                versionNumber = plan.VersionNumber,
                isCurrent = plan.IsCurrent,
                currentVersionId = current?.Id,
                currentVersionNumber = current?.VersionNumber,
                latestVersionNumber,
                title = plan.Title,
                description = plan.Description
            });
        }

        // B4-2: GET /StudyPlans/{id}/enrollment/me
        [HttpGet("{id}/Enrollment/Me")]
        public async Task<IActionResult> GetEnrollmentMe([FromRoute] Guid id, [FromServices] Sciencetopia.Services.Cohorts.ICohortService cohortService)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var dto = await cohortService.GetEnrollmentForUserAsync(id, userId, HttpContext.RequestAborted);
            return Ok(dto);
        }

        // B4-5: GET /StudyPlans/{id}/joinable-cohorts
        [HttpGet("{id}/JoinableCohorts")]
        public async Task<IActionResult> GetJoinableCohorts([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            if (!await _perm.CanReadAsync(userId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            List<dynamic> cohorts;
            try
            {
                cohorts = await _db.Cohorts.AsNoTracking()
                    .Join(_db.CohortOfferings.AsNoTracking(),
                        c => c.CurrentOfferingId,
                        o => o.Id,
                        (c, o) => new { Cohort = c, Offering = o })
                    .Join(_db.StudyGroupStudyPlans.AsNoTracking(),
                        co => co.Offering.StudyGroupStudyPlanId,
                        sgsp => sgsp.Id,
                        (co, sgsp) => new
                        {
                            co.Cohort.Id,
                            co.Cohort.Title,
                            co.Cohort.StudyGroupId,
                            co.Cohort.Visibility,
                            sgsp.StudyPlanStableId
                        })
                    .Where(x => x.StudyPlanStableId == stableId)
                    .Select(x => new { x.Id, x.Title, x.StudyGroupId, x.Visibility })
                    .Cast<dynamic>()
                    .ToListAsync();
            }
            catch (Exception ex) when (IsMissingObjectException(ex, "StudyGroupStudyPlans"))
            {
                cohorts = new List<dynamic>();
            }

            var alreadyActive = await HasActivePlanEnrollmentByStableIdCompatAsync(userId, stableId);

            var groupScoped = new List<object>();
            var pub = new List<object>();

            foreach (var c in cohorts)
            {
                var reason = alreadyActive ? "alreadyInPlan" : null;
                var canJoin = !alreadyActive;
                if (c.StudyGroupId.HasValue && c.StudyGroupId.Value != Guid.Empty)
                {
                    var isMember = await IsGroupMemberAsync(userId, c.StudyGroupId.Value);
                    if (!isMember) { canJoin = false; reason = reason ?? "notGroupMember"; }
                    groupScoped.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
                else if (string.Equals(c.Visibility, "public", StringComparison.OrdinalIgnoreCase))
                {
                    pub.Add(new { id = c.Id, title = c.Title, canJoin, reason });
                }
            }

            return Ok(new { groupScoped, @public = pub, soloAvailable = !alreadyActive });
        }

        [HttpGet("{id}/Permissions/Effective")]
        public async Task<IActionResult> GetEffectivePermission([FromRoute] Guid id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(userId)) return Unauthorized();
            var role = await _perm.GetEffectivePlanRoleAsync(userId, id);
            return Ok(new { role = role.ToString() });
        }

        

        public class ShareUserRequest { public string? UserId { get; set; } public PlanRole Role { get; set; } }

        [HttpPost("{id}/Share/User")]
        public async Task<IActionResult> ShareToUser([FromRoute] Guid id, [FromBody] ShareUserRequest req)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();
            if (string.IsNullOrEmpty(req.UserId)) return BadRequest("UserId required");

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanStableId == stableId && r.UserId == req.UserId);
            if (existing == null)
            {
                _db.StudyPlanUserRoles.Add(new StudyPlanUserRole { PlanStableId = stableId, UserId = req.UserId!, Role = req.Role });
            }
            else
            {
                existing.Role = req.Role;
            }
            await _db.SaveChangesAsync();
            await _perm.InvalidateAsync(req.UserId!, id, HttpContext.RequestAborted);
            return Ok();
        }

        [HttpDelete("{id}/Share/User/{userId}")]
        public async Task<IActionResult> UnshareFromUser([FromRoute] Guid id, [FromRoute] string userId)
        {
            var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            if (string.IsNullOrEmpty(actorId)) return Unauthorized();
            if (!await _perm.CanShareAsync(actorId, id)) return Forbid();

            var stableId = await _db.StudyPlans.AsNoTracking()
                .Where(p => p.Id == id)
                .Select(p => p.StableId == Guid.Empty ? p.Id : p.StableId)
                .FirstOrDefaultAsync();
            if (stableId == Guid.Empty) return NotFound();

            var existing = await _db.StudyPlanUserRoles.FirstOrDefaultAsync(r => r.PlanStableId == stableId && r.UserId == userId);
            if (existing != null)
            {
                _db.StudyPlanUserRoles.Remove(existing);
                await _db.SaveChangesAsync();
                await _perm.InvalidateAsync(userId, id, HttpContext.RequestAborted);
            }
            return Ok();
        }
    }
}
