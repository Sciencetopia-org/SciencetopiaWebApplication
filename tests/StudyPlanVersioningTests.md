Test Skeleton: StudyPlan Versioning & Group Rollforward

Scenarios
- Publish v1: Create plan with lessons -> LockfileJson matches snapshots
- Create draft v2: Outline only with lessonStableIds; publish resolves to current lesson versions or fails on missing
- Diff: v1 vs v2 yields added/removed/upgraded/downgraded/reordered changes
- Group head-status: detects newer head and returns diff summary
- Rollforward/rollback: updates StudyGroupStudyPlan.PinnedVersionNumber and PlanVersionId; respects same planStableId

Setup
- Seed one StudyPlan v1 with 3 lessons; ensure each lesson has current version
- Seed StudyGroupStudyPlan mapping pinned to v1

Tests
1) GET /api/study-plans/{stableId}: returns current v1 with lockfile
2) POST /api/study-plans/{stableId}/versions -> returns draft versionId
3) PATCH /api/study-plans/versions/{versionId} with reordered/added outline -> 200
4) POST /api/study-plans/versions/{versionId}:publish -> 200; v2 current, v1 archived
5) GET /api/study-plans/{stableId}/versions/{from}/diff/{to} -> change list
6) GET /api/study-groups/{id}/head-status -> hasNewerVersion true
7) GET /api/study-groups/{id}/rollforward:preview?toVersionId=... -> full diff
8) POST /api/study-groups/{id}/study-plan:rollforward -> pinned updated
9) POST /api/study-groups/{id}/study-plan:rollback -> pinned set to old

