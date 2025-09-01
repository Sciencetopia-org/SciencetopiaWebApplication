// Migration to simplified enrollment model
// Idempotent: safe to rerun

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

// 3) For each (user, cohort), ensure ENROLLED_IN to cohort's OF_VERSION
MATCH (u:User)-[:IN_COHORT]->(c:Cohort)-[:OF_VERSION]->(pv:PlanVersion)
MERGE (u)-[:ENROLLED_IN]->(pv);

// 4) Remove legacy ACTIVE_IN/PlanEnrollment structures (optional, comment out for dry run)
MATCH (e:PlanEnrollment)-[r:ACTIVE_IN]->(:Cohort)
DELETE r;
DETACH DELETE e;

// 5) Remove legacy PINNED_TO edges (now using OF_VERSION)
MATCH (:Cohort)-[r:PINNED_TO]->(:PlanVersion)
DELETE r;
