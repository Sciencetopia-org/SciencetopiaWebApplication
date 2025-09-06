using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Neo4j.Driver;
using Sciencetopia.Data;
using Sciencetopia.Models;

namespace Sciencetopia.Services
{
    public class PlanSharingService
    {
        private readonly ApplicationDbContext _db;
        private readonly IDriver _neo4j;

        public PlanSharingService(ApplicationDbContext db, IDriver neo4j)
        {
            _db = db;
            _neo4j = neo4j;
        }

        // Get shared plans for a StudyGroup
        public async Task<List<StudyGroupStudyPlan>> GetSharedPlansForStudyGroupAsync(string studyGroupId)
        {
            var sgId = Guid.Parse(studyGroupId);
            var planIds = await _db.StudyGroupStudyPlans
                .AsNoTracking()
                .Where(x => x.StudyGroupId == sgId)
                .ToListAsync();
            return planIds;
        }

        // Share to StudyGroup (upsert)
        public async Task<StudyGroupStudyPlan> ShareToStudyGroupAsync(string studyGroupId, string studyPlanId, string permission, bool autoEnroll, bool useDraftFlow, string? createdBy)
        {
            var sgId = Guid.Parse(studyGroupId);
            var spId = Guid.Parse(studyPlanId);

            var existing = await _db.StudyGroupStudyPlans
                .FirstOrDefaultAsync(x => x.StudyGroupId == sgId && x.StudyPlanId == spId);

            if (existing == null)
            {
                existing = new StudyGroupStudyPlan
                {
                    StudyGroupId = sgId,
                    StudyPlanId = spId,
                    Permission = permission,
                    AutoEnroll = autoEnroll,
                    UseDraftFlow = useDraftFlow,
                    CreatedBy = createdBy,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };
                _db.StudyGroupStudyPlans.Add(existing);
            }
            else
            {
                existing.Permission = permission;
                existing.AutoEnroll = autoEnroll;
                existing.UseDraftFlow = useDraftFlow;
                existing.UpdatedDate = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            // Neo4j relationship
            using var session = _neo4j.AsyncSession();
            var cypher = @"
MERGE (p:StudyPlan {id: $planId})
MERGE (g:StudyGroup {id: $groupId})
MERGE (g)-[r:SHARES_PLAN]->(p)
SET r.permission = $permission,
    r.autoEnroll = $autoEnroll,
    r.useDraftFlow = $useDraftFlow,
    r.updatedAt = timestamp()
RETURN id(r) as relId;";
            await session.RunAsync(cypher, new { planId = studyPlanId, groupId = studyGroupId, permission, autoEnroll, useDraftFlow });

            if (autoEnroll)
            {
                // AutoEnroll members to the plan's current (or latest) PlanVersion
                // Resolve version from SQL
                long? pinned = await _db.StudyPlans.AsNoTracking().Where(p => p.Id == spId).Select(p => p.CurrentVersionId).FirstOrDefaultAsync();
                int versionNumber;
                if (pinned.HasValue)
                {
                    versionNumber = await _db.StudyPlanVersions.Where(v => v.Id == pinned.Value).Select(v => v.VersionNumber).FirstOrDefaultAsync();
                }
                else
                {
                    versionNumber = await _db.StudyPlanVersions.Where(v => v.StudyPlanId == spId).OrderByDescending(v => v.VersionNumber).Select(v => v.VersionNumber).FirstOrDefaultAsync();
                }

                var enrollCypher = @"
MATCH (g:StudyGroup {id:$groupId})<-[:MEMBER_OF]-(u:User)
MERGE (v:PlanVersion {studyPlanId:$planId, versionNumber:$versionNumber})
MERGE (u)-[:ENROLLED_IN]->(v);";
                await session.RunAsync(enrollCypher, new { groupId = studyGroupId, planId = studyPlanId, versionNumber });
            }

            return existing;
        }

        public async Task<bool> UnshareFromStudyGroupAsync(string studyGroupId, string studyPlanId)
        {
            var sgId = Guid.Parse(studyGroupId);
            var spId = Guid.Parse(studyPlanId);

            var existing = await _db.StudyGroupStudyPlans
                .FirstOrDefaultAsync(x => x.StudyGroupId == sgId && x.StudyPlanId == spId);
            if (existing != null)
            {
                _db.StudyGroupStudyPlans.Remove(existing);
                await _db.SaveChangesAsync();
            }

            using var session = _neo4j.AsyncSession();
            var cypher = @"
MATCH (:StudyGroup {id:$groupId})-[r:SHARES_PLAN]->(:StudyPlan {id:$planId})
DELETE r";
            await session.RunAsync(cypher, new { groupId = studyGroupId, planId = studyPlanId });
            return true;
        }

        // Permission calculation
        public async Task<object> GetEffectivePermissionsAsync(string planId, string userId)
        {
            using var session = _neo4j.AsyncSession();

            // Owner check
            var ownerQuery = @"MATCH (u:User {id:$userId})-[:CREATED]->(p:StudyPlan {id:$planId}) RETURN COUNT(p) > 0 AS isOwner";
            var ownerRes = await session.RunAsync(ownerQuery, new { userId, planId });
            var isOwner = (await ownerRes.SingleAsync())[0].As<bool>();

            // Collect group-based permissions
            var permQuery = @"
MATCH (u:User {id:$userId})- [m:MEMBER_OF]-> (sg:StudyGroup)- [r:SHARES_PLAN]-> (p:StudyPlan {id:$planId})
RETURN collect({groupId: sg.id, permission: r.permission, role: m.role}) AS sources";
            var permRes = await session.RunAsync(permQuery, new { userId, planId });
            var sources = (await permRes.SingleAsync())[0].As<List<object>>();

            // Evaluate
            var maxLevel = 0; // view=1, comment=2, edit=3, admin=4
            bool canView = false, canComment = false, canEdit = false, isAdmin = false;
            var sourceList = new List<Dictionary<string, string?>>();

            foreach (var s in sources)
            {
                if (s is IDictionary<string, object> d)
                {
                    var perm = d.ContainsKey("permission") ? d["permission"]?.ToString() ?? "view" : "view";
                    var role = d.ContainsKey("role") ? d["role"]?.ToString() : null;
                    var gid = d.ContainsKey("groupId") ? d["groupId"]?.ToString() : null;
                    sourceList.Add(new Dictionary<string, string?> { { "type", "StudyGroup" }, { "groupId", gid }, { "permission", perm }, { "role", role } });

                    int lvl = perm switch { "admin" => 4, "edit" => 3, "comment" => 2, _ => 1 };
                    maxLevel = Math.Max(maxLevel, lvl);
                    canView = true;
                    if (lvl >= 2) canComment = true;
                    if ((role == "moderator" || role == "manager" || role == "owner") && (lvl >= 3)) canEdit = true;
                    if (lvl >= 4) isAdmin = true;
                }
            }

            if (isOwner)
            {
                canView = canComment = canEdit = true;
                isAdmin = true;
                sourceList.Add(new Dictionary<string, string?> { { "type", "Owner" }, { "groupId", null }, { "permission", "admin" }, { "role", null } });
            }

            return new
            {
                canView,
                canComment,
                canEdit,
                isAdmin,
                sources = sourceList
            };
        }
    }
}
