// Migration to simplified enrollment model
// Idempotent: safe to rerun

// 0) Normalize StudyGroup membership role values to canonical set
MATCH (:User)-[r:MEMBER_OF]->(:StudyGroup)
SET r.role = CASE toLower(coalesce(r.role, 'member'))
  WHEN 'owner' THEN 'owner'
  WHEN 'admin' THEN 'admin'
  WHEN 'manager' THEN 'admin'
  ELSE 'member'
END;

// 1) Ensure Cohort has OF_VERSION to PlanVersion (from existing PINNED_TO or study plan linkage)
MATCH (c:Cohort)
OPTIONAL MATCH (c)-[:OF_VERSION]->(:PlanVersion)
WITH c
WHERE NOT exists((c)-[:OF_VERSION]->(:PlanVersion))
OPTIONAL MATCH (c)-[:PINNED_TO]->(pv1:PlanVersion)
WITH c, pv1
CALL {
  WITH c, pv1
  WITH c, pv1 WHERE pv1 IS NOT NULL
  MERGE (c)-[:OF_VERSION]->(pv1)
  RETURN 1 AS done
}
CALL {
  WITH c, pv1
  WITH c WHERE pv1 IS NULL
  MATCH (c)-[:FOR_PLAN]->(p:StudyPlan)
  // fallback: latest version by VersionNumber
  OPTIONAL MATCH (pv2:PlanVersion {studyPlanId: p.id})
  WITH c, p, pv2
  ORDER BY pv2.versionNumber DESC
  WITH c, collect(pv2)[0] AS latest
  FOREACH (_ IN CASE WHEN latest IS NULL THEN [] ELSE [1] END |
    MERGE (c)-[:OF_VERSION]->(latest)
  )
  RETURN 1 AS done2
}
RETURN count(c) AS cohortsProcessed;

// 2) Collapse cohort membership to single relation IN_COHORT
// Convert PARTICIPATES_IN to IN_COHORT
MATCH (u:User)-[r:PARTICIPATES_IN]->(c:Cohort)
MERGE (u)-[:IN_COHORT]->(c)
DELETE r;

// Deduplicate IN_COHORT per (user, plan)
MATCH (u:User)-[r:IN_COHORT]->(c:Cohort)-[:FOR_PLAN]->(p:StudyPlan)
WITH u, p, collect(r) AS rels
WHERE size(rels) > 1
FOREACH (rel IN rels[1..] | DELETE rel);

// 3) For each (user, cohort), ensure ENROLLED_IN to cohort's OF_VERSION
MATCH (u:User)-[:IN_COHORT]->(c:Cohort)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u)-[:ENROLLED_IN]->(pv);

// Deduplicate ENROLLED_IN per (user, PlanVersion)
MATCH (u:User)-[r:ENROLLED_IN]->(pv:PlanVersion)
WITH u, pv, collect(r) AS rels
WHERE size(rels) > 1
FOREACH (rel IN rels[1..] | DELETE rel);

// 4) Remove legacy ACTIVE_IN/PlanEnrollment structures (optional, comment out for dry run)
MATCH (e:PlanEnrollment)-[r:ACTIVE_IN]->(:Cohort)
DELETE r;
DETACH DELETE e;

// 5) Remove legacy PINNED_TO edges (now using OF_VERSION)
MATCH (:Cohort)-[r:PINNED_TO]->(:PlanVersion)
DELETE r;
