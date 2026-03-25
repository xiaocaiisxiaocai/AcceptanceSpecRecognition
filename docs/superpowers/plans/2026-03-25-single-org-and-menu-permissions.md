# 单组织与菜单权限改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将系统用户的组织模型统一收敛为单组织，并为顶层导航容器补齐独立菜单权限。

**Architecture:** 保留 `AuthUserOrgUnits` 关系表，但通过迁移清洗历史多组织数据并在 `UserId` 上建立唯一约束。后端接口、认证上下文、数据范围服务和前端系统用户页面统一切换到单个 `orgUnitId` 字段；权限类型扩展为 `Menu/Page/Button/Api`，由权限种子、权限字典和前端路由过滤共同支持菜单显隐。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、Vue 3、TypeScript、Pinia、Element Plus

---

### Task 1: 先用测试锁定单组织迁移和系统用户接口契约

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/SystemUsersTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthDataScopeServiceTests.cs`
- Create: `tests/AcceptanceSpecSystem.Data.Tests/AuthUserOrgUnitSingleOrgPolicyTests.cs`

- [x] **Step 1: 为单组织迁移规则补失败测试**

覆盖以下行为：
- 历史多组织且仅一条 `IsPrimary = true` 时，迁移后只保留该条
- 历史多组织但没有唯一主组织时，迁移后保留最早一条
- 历史无组织用户迁移后补到根组织

- [x] **Step 2: 为系统用户旧接口口径补失败测试**

覆盖以下行为：
- 创建用户提交 `orgUnitId` 成功
- 创建或更新用户提交 `orgUnitIds` / `primaryOrgUnitId` 旧字段失败
- 系统用户列表与详情不再要求 `orgUnits[]`

- [x] **Step 3: 运行定向测试，确认失败**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers|FullyQualifiedName~AuthDataScopeService" -p:UseSharedCompilation=false
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserOrgUnitSingleOrgPolicyTests" -p:UseSharedCompilation=false
```

Expected: 新增测试失败，且失败原因是当前实现仍接受多组织字段或仍按主组织语义工作。

### Task 2: 实现数据库迁移和后端单组织模型

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Migrations/AppDbContextModelSnapshot.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Entities/AuthorizationEntities.cs`
- Create: `src/AcceptanceSpecSystem.Data/Entities/AuthUserOrgUnitSingleOrgPolicy.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_EnforceSingleOrgPerUser.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/SystemUserDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SystemUsersController.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/SystemUserRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/ISystemUserRepository.cs`

- [x] **Step 1: 编写单组织裁剪策略帮助类**

实现与单角色改造一致的固定规则：
- 唯一主组织优先
- 否则按 `CreatedAt ASC, Id ASC`

- [x] **Step 2: 编写迁移，先清洗历史组织关系再加唯一约束**

迁移逻辑要求：
- 每个用户最多保留一条组织关系
- 无组织用户补到公司根组织
- 最后为 `AuthUserOrgUnits.UserId` 增加唯一索引

- [x] **Step 3: 将系统用户 DTO 和控制器改为 `orgUnitId`**

调整：
- `SystemUserDto` 改为 `orgUnitId` / `orgUnitName`
- 创建、更新请求只接受 `orgUnitId`
- 移除 `primaryOrgUnitId` / `orgUnitIds`
- `ToDto` 和组织校验逻辑只处理单组织

- [x] **Step 4: 运行 Task 1 相关测试，确认转绿**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers" -p:UseSharedCompilation=false
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserOrgUnitSingleOrgPolicyTests" -p:UseSharedCompilation=false
```

Expected: 单组织迁移与系统用户接口测试通过。

### Task 3: 收口认证访问上下文和数据范围服务

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthDataScopeService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthDataScopeServiceTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/Infrastructure/ApiWebApplicationFactory.cs`

- [x] **Step 1: 先让测试覆盖唯一组织上下文**

补或完善断言：
- 认证访问上下文只取一个 `OrgUnitId`
- 数据范围服务不再依赖多组织集合和主组织切换
- 默认种子用户只有一条组织关系

- [x] **Step 2: 最小实现单组织上下文**

调整：
- `AuthAccessService` 改为只读取唯一组织关系
- `AuthDataScopeService` 改为基于唯一组织关系计算当前组织及子树
- `AuthUserSeedService` 不再维护多组织和“设置主组织”逻辑

- [x] **Step 3: 运行认证与数据范围测试**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~AuthDataScopeService|FullyQualifiedName~AuthTests" -p:UseSharedCompilation=false
```

Expected: 数据范围和认证链路在单组织模型下通过。

### Task 4: 扩展菜单权限类型和权限种子

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Entities/Enums.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/AuthPermissionsController.cs`
- Modify: `web/src/api/auth-permission.ts`
- Modify: `web/src/views/rbac/permissions/index.vue`
- Modify: `web/src/views/config/auth-roles/index.vue`

- [x] **Step 1: 为菜单权限补失败测试或断言**

覆盖以下行为：
- 权限字典可返回菜单权限项
- `admin` 拥有菜单权限
- 角色管理页能按类型筛选菜单权限

- [x] **Step 2: 扩展权限类型与种子数据**

实现：
- `PermissionType` 增加 `Menu`
- 为 `/config`、`/rbac` 等顶层容器补 `menu:*` 权限码
- `admin` 自动拥有全部菜单权限
- `common` 拥有最小业务必需菜单权限

- [x] **Step 3: 调整权限字典和角色管理页面**

实现：
- 权限字典支持显示“菜单权限”
- 角色管理页支持全选/清空菜单权限
- 权限筛选文案同步更新

- [x] **Step 4: 运行菜单权限相关测试**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~AuthPermissions|FullyQualifiedName~AuthRoles" -p:UseSharedCompilation=false
```

Expected: 菜单权限种子和权限字典相关测试通过。

### Task 5: 改前端系统用户与动态路由权限过滤

**Files:**
- Modify: `web/src/api/system-user.ts`
- Modify: `web/src/store/types.ts`
- Modify: `web/src/utils/auth.ts`
- Modify: `web/src/utils/sso.ts`
- Modify: `web/src/views/config/system-users/index.vue`
- Modify: `web/src/router/modules/config.ts`
- Modify: `web/src/router/modules/rbac.ts`
- Modify: `web/src/router/utils.ts`
- Modify: `web/src/utils/permission.ts`
- Modify: `web/src/store/modules/permission.ts`

- [x] **Step 1: 调整前端类型定义和缓存结构**

把用户组织相关字段从：
- `orgUnits[]`
- `orgUnitIds[]`
- `primaryOrgUnitId`

改为：
- `orgUnitId`
- `orgUnitName`

- [x] **Step 2: 调整系统用户页面为单组织选择**

改动包括：
- 组织选择从多选改为单选
- 移除“主组织”表单项
- 列表只展示一个组织
- 新增/编辑表单校验改为必须填写 `orgUnitId`

- [x] **Step 3: 调整顶层菜单路由权限**

改动包括：
- `/config`、`/rbac` 顶层路由加 `menu:*` 权限码
- 菜单过滤逻辑按菜单权限过滤容器节点
- 页面节点继续按 `page:*` 过滤

- [x] **Step 4: 运行前端类型检查**

Run:
```powershell
pnpm --dir web typecheck
```

Expected: TypeScript 类型检查通过。

### Task 6: 全量回归并收尾

**Files:**
- Modify: `openspec/changes/update-single-org-and-menu-permissions/tasks.md`

- [x] **Step 1: 运行后端、前端与 OpenSpec 验证**

Run:
```powershell
openspec validate update-single-org-and-menu-permissions --strict
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers|FullyQualifiedName~AuthDataScopeService|FullyQualifiedName~AuthRoles|FullyQualifiedName~AuthTests" -p:UseSharedCompilation=false
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserOrgUnitSingleOrgPolicyTests" -p:UseSharedCompilation=false
dotnet build src\AcceptanceSpecSystem.Api\AcceptanceSpecSystem.Api.csproj -c Release -p:UseSharedCompilation=false
pnpm --dir web typecheck
```

Expected: 全部通过。

- [x] **Step 2: 更新 OpenSpec 任务状态**

把 `openspec/changes/update-single-org-and-menu-permissions/tasks.md` 全部改为已完成。

- [x] **Step 3: 提交实现**

```powershell
git add <relevant-files>
git commit -m "feat: 强制系统用户单组织并增加菜单权限"
```
