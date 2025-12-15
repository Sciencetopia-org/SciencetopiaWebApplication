# Sciencetopia 数据结构设计方案（当前实现版）

## 1. 总体原则
- **双数据库架构**：SQL 作为 Source of Truth（事实、版本、权限），Neo4j 作为结构投影（路径、推荐、联结）。
- **Group-first**：所有学习活动、权限、学习计划绑定到 Group 语境内。
- **版本化**：StudyPlan / Lesson / Tag / KnowledgeNode 均以 StableId + VersionNumber 管理；`IsCurrent` + `Status`（Draft/Active/Archived）。
- **L10n 可选**：实体可挂 L10nSetId；现有 Name/Description 仍保留为回退。

## 2. Groups 体系
### 2.1 祖先表
`StudyGroups.Groups` (`GroupEntity`)
- `Id` (PK, GUID)
- `Kind` (`StudyGroup`|`CohortGroup`|`OrgGroup`…)
- `CreatedByUserId`, `CreatedAt`, `UpdatedAt`, `RowVersion`

### 2.2 StudyGroup
`StudyGroups.StudyGroups` (`StudyGroupEntity`)
- `Id` (PK, FK -> Groups.Id)
- `NameL10nSetId`, `DescriptionL10nSetId`（可空）
- `Name`, `Description`, `ImageUrl`, `Status`
- `Visibility` (Public/Private/Unlisted，默认 Private)
- `JoinPolicy` (Open/Request/InviteOnly，默认 Request)
- `SettingsJson`

### 2.3 CohortGroup
`StudyGroups.CohortGroups` (`CohortGroupEntity`)
- `Id` (PK, FK -> Groups.Id)
- `ParentGroupId` (FK -> Groups.Id)
- `StudyPlanStableId`, `StudyPlanVersionId`（可空）
- `NameL10nSetId`（可空）
- `StartAt`, `EndAt`
- `SettingsJson`

### 2.4 Group 成员关系
`StudyGroups.GroupMembers` (`GroupMemberEntity`)
- `Id` (PK)
- `GroupId` (FK -> StudyGroups.Id)
- `UserId` (string, nvarchar(450))
- `Role` (`GroupRole` 枚举：Member/Learner/TA/Admin/Owner；Manager 为 Admin 兼容别名)
- `Status` (Active/Pending/Banned…)
- `JoinedAt`, `LeftAt`
- 唯一索引 `(GroupId, UserId)`

### 2.5 Group ↔ StudyPlan 关系
`StudyGroups.StudyGroupStudyPlans`
- `Id` (PK)
- `StudyGroupId` (FK)
- `StudyPlanStableId`
- `PinnedVersionNumber`（可空）
- `ActivePlanVersionId`（列名由 `PlanVersionId` 映射而来）
- `ResolveToHead` (bool)
- `RelationType` (Offer/Adopt/Default，默认 Adopt)
- `SettingsJson`
- `Permission` (view/comment/edit/admin)
- `AutoEnroll` (bool)
- `CreatedBy`, `CreatedDate`, `UpdatedDate`
- 唯一索引 `(StudyGroupId, StudyPlanStableId)`

### 2.6 版本切换审计
`StudyGroups.GroupPlanSwitches`
- `Id`, `StudyGroupId`, `PlanStableId`
- `FromVersionId/Number`, `ToVersionId/Number`
- `EffectiveAt`, `ExecutedAt`
- `Actor`, `Reason`, `CreatedAt`

## 3. StudyPlan / Lesson 版本模型
### 3.1 StudyPlans
`StudyPlans.StudyPlans` (`StudyPlanEntity`)
- `Id` (PlanVersionId, PK)
- `StableId`
- `VersionNumber`
- `Status` (Draft/Active/Archived)
- `IsCurrent`
- `PublishedAt`, `RetiredAt`, `CreatedAt`, `ApprovedAt`
- `CreatedBy`, `ApprovedBy`
- `CreatorId` (GUID)
- `RowVersion`
- `Title`, `Description`
- `MetadataJson`（结构化设置/统计）
- `LockfileJson`（发布快照）
- 索引：`(StableId, VersionNumber)`，`StableId` + `IsCurrent` 过滤唯一

### 3.2 Lessons
`StudyPlans.Lessons` (`LessonEntity`)
- `Id` (LessonVersionId, PK)
- `StableId`
- `VersionNumber`
- `Status` (Draft/Active/Archived)
- `IsCurrent`
- `PublishedAt`, `RetiredAt`, `CreatedAt`, `ApprovedAt`
- `CreatedBy`, `ApprovedBy`
- `RowVersion`
- `Title`, `Description`
- **Plan 绑定**：`StudyPlanId` (FK -> StudyPlans.Id)，`OrderIndex`
- `Kind` (Video/Reading/Exercise，默认 Reading)
- `MetadataJson`（当前存储 stepType 等）
- 索引：`(StableId, VersionNumber)`；`StableId` + `IsCurrent` 过滤唯一；`(StudyPlanId, StableId)` 过滤唯一

### 3.3 Plan ↔ Lesson 快照
`StudyPlans.StudyPlanLessonSnapshots`
- 复合键 `(StudyPlanStableId, StudyPlanVersionNumber, LessonStableId, LessonVersionNumber)`
- `StepType`（Prerequisite/MainCurriculum/AdvancedTopic）
- `StepOrder`
- 用于锁文件与重建顺序的 SQL Source of Truth。

### 3.4 标签与资源
- Plan 标签：`StudyPlans.StudyPlanTagAssignments` (`StudyPlanStableId`, `StudyPlanVersionNumber`, `TagStableId`, `AssignedAt`)
- Lesson 标签：`StudyPlans.LessonTagAssignments` (`LessonStableId`, `LessonVersionNumber`, `TagStableId`, `AssignedAt`)
- 资源：`KnowledgeGraph.Resources`，Lesson 资源关联仍由 Neo4j 维护关联，SQL 存储资源内容。

## 4. Enrollment（用户学习绑定）
`StudyPlans.UserStudyPlanEnrollments`
- `Id` (PK)
- `UserId`
- `GroupId`
- `StudyPlanStableId`
- `CohortGroupId`（可空）
- `PlanVersionId`
- `Role` (Learner/TA/Instructor)
- `Status` (Active/Completed/Dropped)
- `ProgressJson`
- `CreatedAt`, `UpdatedAt`
- 索引：`(UserId, StudyPlanStableId)`、`GroupId`、`CohortGroupId`

## 5. 权限模型（SQL 为主）
- Group 角色：`GroupRole` 枚举（Member/Learner/TA/Admin/Owner），Manager 兼容为 Admin。
- Plan 角色：`PlanRole` 枚举（Viewer/Commenter/Editor/Owner）；`StudyPlanUserRoles` 按 StableId 赋权。
- 权限判定：Plan 可见性 = 创作者/直接角色/Group 共享 + membership/Public；Cohort 操作由 Group Admin/Owner 管理。

## 6. L10n 约定（简述）
- 采用 `L10nSets / L10nItems / L10nSetItems / L10nEntityMappings`（当前代码已存在表名：`L10nSets`, `L10nItems`, `NodeL10nSets`, `TagL10nSets` 等）。
- StudyGroup 支持 Name/Description L10nSetId 升级；其他实体可按需要扩展 `L10nEntityMappings`。

## 7. Neo4j 同步（当前代码思路）
- Group 与成员：`(User)-[:MEMBER_OF {role,status}]->(StudyGroup)`。
- Group ↔ Plan：`(:StudyGroup)-[:SHARES_PLAN]->(:StudyPlan)`（旧存在）；需要保持与 SQL `StudyGroupStudyPlans` 同步。
- Plan ↔ Lesson：`(StudyPlan)-[:HAS_STEP {type,order}]->(Lesson)`；Lesson ↔ Resource/KnowledgeNode 关联在图里维护。
- Cohort：`(Cohort)-[:FOR_PLAN]->(StudyPlan)`，成员 `(:User)-[:IN_COHORT]->(:Cohort)`。
- 推荐使用 Outbox/后台 Worker 做双写同步（规范提议）。

## 8. 仍需补充/迁移的工作
- 运行 EF Migration：创建新表/列（Groups/CohortGroups/GroupMembers/UserStudyPlanEnrollments，Lessons 的 StudyPlanId/Kind/MetadataJson，StudyGroupStudyPlans 的 RelationType/SettingsJson/ActivePlanVersionId 等）并回填数据、状态标准化（Current→Active）。
- Neo4j 角色标签需与新枚举对齐（manager→admin/owner）。
- Enrollment 流程落地：在业务流程中写入 `UserStudyPlanEnrollments`，并保持与图同步。
- L10n 映射：为 StudyGroup/Plan/Lesson 等需要多语言的字段登记 `L10nEntityMappings`。

## 9. 目录索引
- 主要实体代码：`Models/Groups/*.cs`, `Models/StudyGroup/StudyGroupEntity.cs`, `Models/StudyPlan/StudyPlan.cs`, `Models/StudyPlan/UserStudyPlanEnrollment.cs`, `Models/Groups/StudyGroupStudyPlan.cs`
- 数据上下文：`Data/ApplicationDbContext.cs`
- 服务与仓储：`Services/StudyGroup/StudyGroupService.cs`, `Services/StudyPlan/StudyPlanService.cs`, `Services/Plans/PermissionService.cs`, `Services/Cohorts/ICohortService.cs`, `Repositories/Sql/IStudyPlanRepository.cs`
