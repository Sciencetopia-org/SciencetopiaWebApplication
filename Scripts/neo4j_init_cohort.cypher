CREATE CONSTRAINT user_id     IF NOT EXISTS FOR (n:User)        REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT res_id      IF NOT EXISTS FOR (n:Resource)    REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT plan_id     IF NOT EXISTS FOR (n:StudyPlan)   REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT les_id      IF NOT EXISTS FOR (n:Lesson)      REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT kn_id       IF NOT EXISTS FOR (n:KnowledgeNode) REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT cohort_id   IF NOT EXISTS FOR (n:Cohort)      REQUIRE n.id IS UNIQUE;
CREATE CONSTRAINT group_id    IF NOT EXISTS FOR (n:StudyGroup)  REQUIRE n.id IS UNIQUE;

// PlanVersion anchor uniqueness on (studyPlanId, versionNumber)
CREATE CONSTRAINT plan_version_unique IF NOT EXISTS FOR (v:PlanVersion)
REQUIRE (v.studyPlanId, v.versionNumber) IS UNIQUE;
