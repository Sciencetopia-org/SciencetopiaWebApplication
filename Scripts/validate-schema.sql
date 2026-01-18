SET NOCOUNT ON;

-- Cohorts must map to Groups with Type = Cohort
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[Cohorts] c
    LEFT JOIN [Groups].[Groups] g ON g.[GroupId] = c.[GroupId]
    WHERE g.[GroupId] IS NULL OR g.[Type] <> N'Cohort'
)
BEGIN
    THROW 50000, 'Invariant failed: Cohorts.GroupId must exist in Groups with Type = Cohort.', 1;
END

-- StudyGroups must map to Groups with Type = StudyGroup
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[StudyGroups] sg
    LEFT JOIN [Groups].[Groups] g ON g.[GroupId] = sg.[GroupId]
    WHERE g.[GroupId] IS NULL OR g.[Type] <> N'StudyGroup'
)
BEGIN
    THROW 50000, 'Invariant failed: StudyGroups.GroupId must exist in Groups with Type = StudyGroup.', 1;
END

-- Cohort enrollments must point to Cohorts
IF EXISTS (
    SELECT 1
    FROM [StudyPlans].[StudyPlanEnrollments] e
    WHERE e.[ScopeType] = N'Cohort'
      AND NOT EXISTS (SELECT 1 FROM [StudyGroups].[Cohorts] c WHERE c.[GroupId] = e.[ScopeId])
)
BEGIN
    THROW 50000, 'Invariant failed: StudyPlanEnrollments ScopeType=Cohort must reference StudyGroups.Cohorts.GroupId.', 1;
END

-- Personal enrollments must point to Groups with Type=PersonalGroup
IF EXISTS (
    SELECT 1
    FROM [StudyPlans].[StudyPlanEnrollments] e
    WHERE e.[ScopeType] = N'Personal'
      AND NOT EXISTS (
          SELECT 1 FROM [Groups].[Groups] g
          WHERE g.[GroupId] = e.[ScopeId] AND g.[Type] = N'PersonalGroup'
      )
)
BEGIN
    THROW 50000, 'Invariant failed: StudyPlanEnrollments ScopeType=Personal must reference Groups.Type=PersonalGroup.', 1;
END

-- Cohorts must have CurrentOfferingId
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[Cohorts]
    WHERE [CurrentOfferingId] IS NULL
)
BEGIN
    THROW 50000, 'Invariant failed: Cohorts.CurrentOfferingId cannot be NULL.', 1;
END

-- Cohorts.CurrentOfferingId must reference CohortOfferings
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[Cohorts] c
    LEFT JOIN [StudyGroups].[CohortOfferings] o ON o.[Id] = c.[CurrentOfferingId]
    WHERE c.[CurrentOfferingId] IS NOT NULL AND o.[Id] IS NULL
)
BEGIN
    THROW 50000, 'Invariant failed: Cohorts.CurrentOfferingId must reference StudyGroups.CohortOfferings.Id.', 1;
END

-- CohortOfferings must map to Cohorts
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[CohortOfferings] o
    LEFT JOIN [StudyGroups].[Cohorts] c ON c.[GroupId] = o.[CohortGroupId]
    WHERE c.[GroupId] IS NULL
)
BEGIN
    THROW 50000, 'Invariant failed: CohortOfferings.CohortGroupId must reference StudyGroups.Cohorts.GroupId.', 1;
END

-- CohortOfferings must map to StudyGroupStudyPlans
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[CohortOfferings] o
    LEFT JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp ON sgsp.[Id] = o.[StudyGroupStudyPlanId]
    WHERE sgsp.[Id] IS NULL
)
BEGIN
    THROW 50000, 'Invariant failed: CohortOfferings.StudyGroupStudyPlanId must reference StudyGroups.StudyGroupStudyPlans.Id.', 1;
END

-- CohortOfferings must map to StudyPlans (PlanVersionId)
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[CohortOfferings] o
    LEFT JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = o.[StudyPlanVersionId]
    WHERE p.[Id] IS NULL
)
BEGIN
    THROW 50000, 'Invariant failed: CohortOfferings.StudyPlanVersionId must reference StudyPlans.StudyPlans.Id.', 1;
END

-- CohortOfferings must align stableId with adoption
IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[CohortOfferings] o
    JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp ON sgsp.[Id] = o.[StudyGroupStudyPlanId]
    JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = o.[StudyPlanVersionId]
    WHERE p.[StableId] <> sgsp.[StudyPlanStableId]
      AND p.[Id] <> sgsp.[StudyPlanStableId]
)
BEGIN
    THROW 50000, 'Invariant failed: CohortOfferings.StudyPlanVersionId must match StudyGroupStudyPlans.StudyPlanStableId.', 1;
END

-- Optional legacy table checks
IF OBJECT_ID(N'[StudyGroups].[GroupMembers]', N'U') IS NOT NULL
    PRINT 'WARN: legacy table StudyGroups.GroupMembers still exists.';
IF OBJECT_ID(N'[StudyPlans].[UserStudyPlanEnrollments]', N'U') IS NOT NULL
    PRINT 'WARN: legacy table StudyPlans.UserStudyPlanEnrollments still exists.';
