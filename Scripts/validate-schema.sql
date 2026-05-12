SET NOCOUNT ON;

IF OBJECT_ID(N'[Groups].[Groups]', N'U') IS NULL
    THROW 50000, 'Invariant failed: Groups.Groups must exist.', 1;

IF OBJECT_ID(N'[Groups].[UserGroups]', N'U') IS NULL
    THROW 50000, 'Invariant failed: Groups.UserGroups must exist.', 1;

IF OBJECT_ID(N'[StudyPlans].[StudyPlans]', N'U') IS NULL
    THROW 50000, 'Invariant failed: StudyPlans.StudyPlans must exist.', 1;

IF OBJECT_ID(N'[StudyPlans].[GroupPlanEnrollments]', N'U') IS NULL
    THROW 50000, 'Invariant failed: StudyPlans.GroupPlanEnrollments must exist for PersonalGroup plan enrollment.', 1;

IF COL_LENGTH(N'StudyPlans.GroupPlanEnrollments', N'Role') IS NULL
    THROW 50000, 'Invariant failed: GroupPlanEnrollments.Role must exist for group-scoped plan permissions.', 1;

IF OBJECT_ID(N'[StudyPlans].[StudyPlanUserRoles]', N'U') IS NOT NULL
    THROW 50000, 'Invariant failed: StudyPlans.StudyPlanUserRoles should not exist; use PersonalGroup GroupPlanEnrollments.Role.', 1;

IF OBJECT_ID(N'[StudyGroups].[StudyGroupStudyPlans]', N'U') IS NULL
    THROW 50000, 'Invariant failed: StudyGroups.StudyGroupStudyPlans must exist for StudyGroup plan adoption.', 1;

IF OBJECT_ID(N'[StudyGroups].[Cohorts]', N'U') IS NULL
    THROW 50000, 'Invariant failed: StudyGroups.Cohorts must exist.', 1;

IF OBJECT_ID(N'[StudyGroups].[CohortOfferings]', N'U') IS NOT NULL
    THROW 50000, 'Invariant failed: StudyGroups.CohortOfferings should not exist; Cohort is one concrete class.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'CurrentOfferingId') IS NOT NULL
    THROW 50000, 'Invariant failed: Cohorts.CurrentOfferingId should not exist.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'StudyGroupId') IS NOT NULL
    THROW 50000, 'Invariant failed: Cohorts.StudyGroupId should not exist; derive it through StudyGroupStudyPlans.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'ParentGroupId') IS NOT NULL
    THROW 50000, 'Invariant failed: Cohorts.ParentGroupId should not exist.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'PlanVersionId') IS NOT NULL
    THROW 50000, 'Invariant failed: Cohorts.PlanVersionId should not exist; use StudyPlanVersionId.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'StudyGroupStudyPlanId') IS NULL
    THROW 50000, 'Invariant failed: Cohorts.StudyGroupStudyPlanId must exist.', 1;

IF COL_LENGTH(N'StudyGroups.Cohorts', N'StudyPlanVersionId') IS NULL
    THROW 50000, 'Invariant failed: Cohorts.StudyPlanVersionId must exist.', 1;

IF OBJECT_ID(N'[Users].[AspNetUsers]', N'U') IS NOT NULL
AND EXISTS (
    SELECT 1
    FROM [Users].[AspNetUsers] u
    WHERE NOT EXISTS (
        SELECT 1
        FROM [Groups].[UserGroups] ug
        JOIN [Groups].[Groups] g ON g.[Id] = ug.[GroupId]
        WHERE ug.[UserId] COLLATE DATABASE_DEFAULT = u.[Id] COLLATE DATABASE_DEFAULT
          AND ug.[Status] COLLATE DATABASE_DEFAULT = N'Active'
          AND g.[Kind] COLLATE DATABASE_DEFAULT = N'PersonalGroup'
          AND g.[CreatedByUserId] COLLATE DATABASE_DEFAULT = u.[Id] COLLATE DATABASE_DEFAULT
    )
)
    THROW 50000, 'Invariant failed: every user must have an active owned PersonalGroup membership.', 1;

IF EXISTS (
    SELECT 1
    FROM [StudyPlans].[GroupPlanEnrollments] e
    JOIN [Groups].[Groups] g ON g.[Id] = e.[GroupId]
    WHERE g.[Kind] COLLATE DATABASE_DEFAULT <> N'PersonalGroup'
)
    THROW 50000, 'Invariant failed: GroupPlanEnrollments must only reference PersonalGroup.', 1;

IF EXISTS (
    SELECT 1
    FROM [StudyPlans].[GroupPlanEnrollments] e
    LEFT JOIN [Groups].[Groups] g ON g.[Id] = e.[GroupId]
    LEFT JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = e.[PlanVersionId]
    WHERE g.[Id] IS NULL OR p.[Id] IS NULL
)
    THROW 50000, 'Invariant failed: GroupPlanEnrollments must reference existing Groups and StudyPlan versions.', 1;

IF EXISTS (
    SELECT 1
    FROM [StudyPlans].[GroupPlanEnrollments] e
    JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = e.[PlanVersionId]
    WHERE e.[VersionPolicy] COLLATE DATABASE_DEFAULT NOT IN (N'Current', N'Pinned')
       OR e.[Status] COLLATE DATABASE_DEFAULT NOT IN (N'Active', N'Archived')
       OR e.[StudyPlanStableId] <> CASE WHEN p.[StableId] = '00000000-0000-0000-0000-000000000000' THEN p.[Id] ELSE p.[StableId] END
)
    THROW 50000, 'Invariant failed: GroupPlanEnrollments values must be valid and PlanVersionId must match StudyPlanStableId.', 1;

IF OBJECT_ID(N'[KnowledgeGraph].[Favorites]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'KnowledgeGraph.Favorites', N'GroupId') IS NULL
        THROW 50000, 'Invariant failed: Favorites.GroupId must exist.', 1;

    IF COL_LENGTH(N'KnowledgeGraph.Favorites', N'UserId') IS NOT NULL
        THROW 50000, 'Invariant failed: Favorites.UserId should not exist; favorites belong to PersonalGroup.', 1;

    EXEC(N'
    IF EXISTS (
        SELECT 1
        FROM [KnowledgeGraph].[Favorites] f
        LEFT JOIN [Groups].[Groups] g ON g.[Id] = f.[GroupId]
        WHERE g.[Id] IS NULL OR g.[Kind] COLLATE DATABASE_DEFAULT <> N''PersonalGroup''
    )
        THROW 50000, ''Invariant failed: Favorites must reference PersonalGroup.'', 1;
    ');
END;

IF EXISTS (
    SELECT 1
    FROM [StudyGroups].[Cohorts] c
    LEFT JOIN [Groups].[Groups] g ON g.[Id] = c.[Id]
    WHERE g.[Id] IS NULL OR g.[Kind] COLLATE DATABASE_DEFAULT <> N'Cohort'
)
    THROW 50000, 'Invariant failed: Cohorts.Id must reference a Groups row with Kind = Cohort.', 1;

EXEC(N'
    IF EXISTS (
        SELECT 1
        FROM [StudyGroups].[Cohorts] c
        LEFT JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp ON sgsp.[Id] = c.[StudyGroupStudyPlanId]
        LEFT JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = c.[StudyPlanVersionId]
        WHERE sgsp.[Id] IS NULL OR p.[Id] IS NULL
    )
        THROW 50000, ''Invariant failed: Cohorts must reference StudyGroupStudyPlans and StudyPlan versions.'', 1;
');

EXEC(N'
    IF EXISTS (
        SELECT 1
        FROM [StudyGroups].[Cohorts] c
        JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp ON sgsp.[Id] = c.[StudyGroupStudyPlanId]
        JOIN [StudyPlans].[StudyPlans] p ON p.[Id] = c.[StudyPlanVersionId]
        WHERE sgsp.[StudyPlanStableId] <> CASE WHEN p.[StableId] = ''00000000-0000-0000-0000-000000000000'' THEN p.[Id] ELSE p.[StableId] END
    )
        THROW 50000, ''Invariant failed: Cohorts.StudyPlanVersionId must match StudyGroupStudyPlans.StudyPlanStableId.'', 1;
');

EXEC(N'
    IF EXISTS (
        SELECT 1
        FROM (
            SELECT DISTINCT
                ug.[UserId],
                ug.[GroupId] AS [CohortId],
                pg.[PersonalGroupId],
                c.[StudyPlanVersionId],
                sgsp.[StudyPlanStableId]
            FROM [Groups].[UserGroups] ug
            JOIN [Groups].[Groups] cg ON cg.[Id] = ug.[GroupId]
                AND cg.[Kind] COLLATE DATABASE_DEFAULT = N''Cohort''
            JOIN [StudyGroups].[Cohorts] c ON c.[Id] = ug.[GroupId]
                AND c.[Status] COLLATE DATABASE_DEFAULT = N''Active''
            JOIN [StudyGroups].[StudyGroupStudyPlans] sgsp ON sgsp.[Id] = c.[StudyGroupStudyPlanId]
            OUTER APPLY (
                SELECT TOP (1) pug.[GroupId] AS [PersonalGroupId]
                FROM [Groups].[UserGroups] pug
                JOIN [Groups].[Groups] g ON g.[Id] = pug.[GroupId]
                WHERE pug.[UserId] COLLATE DATABASE_DEFAULT = ug.[UserId] COLLATE DATABASE_DEFAULT
                  AND g.[Kind] COLLATE DATABASE_DEFAULT = N''PersonalGroup''
                  AND (pug.[Status] IS NULL OR pug.[Status] COLLATE DATABASE_DEFAULT IN (N''Active'', N''active''))
                ORDER BY pug.[JoinedAt]
            ) pg
            WHERE ug.[Status] IS NULL OR ug.[Status] COLLATE DATABASE_DEFAULT IN (N''Active'', N''active'')
        ) cm
        LEFT JOIN [StudyPlans].[GroupPlanEnrollments] e ON e.[GroupId] = cm.[PersonalGroupId]
            AND e.[StudyPlanStableId] = cm.[StudyPlanStableId]
            AND e.[PlanVersionId] = cm.[StudyPlanVersionId]
            AND e.[Status] COLLATE DATABASE_DEFAULT = N''Active''
        WHERE cm.[PersonalGroupId] IS NULL OR e.[Id] IS NULL
    )
        THROW 50000, ''Invariant failed: active Cohort membership must have a matching active PersonalGroup plan enrollment pinned to the cohort version.'', 1;
');

SELECT
    'Schema validation passed' AS [Status],
    (SELECT COUNT(*) FROM [StudyGroups].[Cohorts]) AS [CohortCount],
    (SELECT COUNT(*) FROM [StudyPlans].[GroupPlanEnrollments]) AS [GroupPlanEnrollmentCount];
