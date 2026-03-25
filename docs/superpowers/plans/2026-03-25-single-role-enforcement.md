# 单角色强制改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将系统用户、认证返回与数据库约束统一收敛为单角色模型，彻底移除 `roles` 数组口径。

**Architecture:** 保留 `AuthUserRoles` 关系表，但通过迁移清洗历史数据并增加 `UserId` 唯一约束。后端 DTO、控制器、认证上下文、JWT 与前端系统用户页/登录态统一改为 `roleCode` 单值字段，权限数组保持不变。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、Vue 3、TypeScript、Pinia、Element Plus

---

### Task 1: 先用测试锁定单角色数据库迁移与后端契约

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/SystemUsersControllerTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthApiTests.cs`
- Create: `tests/AcceptanceSpecSystem.Data.Tests/AuthUserRoleSingleRoleMigrationTests.cs`

- [x] **Step 1: 写迁移与接口失败测试**

为以下行为先补失败测试：
- 多角色历史数据迁移后仅保留 `admin`
- 无 `admin` 的多角色历史数据仅保留第一条
- 无角色用户迁移后补到 `common`
- 创建/更新用户提交旧 `roles` 数组时失败
- 登录/刷新返回不再包含 `roles`，改为 `roleCode`

- [x] **Step 2: 运行定向测试，确认失败**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers|FullyQualifiedName~Auth"
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserRoleSingleRoleMigration"
```

Expected: 新增测试失败，且失败原因是现有实现仍接受多角色或仍返回角色数组。

### Task 2: 实现数据库迁移与系统用户单角色接口

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_EnforceSingleRolePerUser.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/SystemUserDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SystemUsersController.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/SystemUserRepository.cs`

- [x] **Step 1: 编写迁移，先清洗数据再加唯一约束**

迁移逻辑要求：
- 每个用户优先保留 `admin`
- 否则按 `CreatedAt ASC, Id ASC` 保留第一条
- 无角色用户补 `common`
- 最后为 `AuthUserRoles.UserId` 增加唯一索引

- [x] **Step 2: 将系统用户 DTO 和控制器改为 `roleCode`**

调整：
- `SystemUserDto` 改为 `roleCode` / `roleName`
- 创建、更新请求只接受 `roleCode`
- `ValidateAdminBoundaryAsync` 改按单角色判断
- `ToDto` 与查询逻辑只输出单角色

- [x] **Step 3: 运行 Task 1 相关测试，确认转绿**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers"
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserRoleSingleRoleMigration"
```

Expected: 迁移与系统用户接口测试通过。

### Task 3: 收口认证上下文、登录返回和 JWT

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthAccessService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/IAuthTokenService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthTokenService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/AuthController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/AuthDtos.cs`

- [x] **Step 1: 先让认证测试覆盖单角色返回**

补或完善测试断言：
- `LoginSuccessData` 只返回 `roleCode`
- `RefreshTokenSuccessData` 只返回 `roleCode`
- JWT 仅写入单个角色声明

- [x] **Step 2: 最小实现认证链路单角色化**

调整：
- `AuthAccessContext` 改用 `RoleCode`
- `AuthTokenUser` 改用 `RoleCode`
- `AuthController` 登录/刷新响应改单值字段
- `AuthTokenService` 只写入一个 `ClaimTypes.Role`

- [x] **Step 3: 运行认证测试，确认通过**

Run:
```powershell
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~Auth"
```

Expected: 登录、刷新与认证上下文测试通过。

### Task 4: 改前端 API、用户缓存和系统用户页面为单角色

**Files:**
- Modify: `web/src/api/system-user.ts`
- Modify: `web/src/api/user.ts`
- Modify: `web/src/store/types.ts`
- Modify: `web/src/store/modules/user.ts`
- Modify: `web/src/utils/auth.ts`
- Modify: `web/src/utils/sso.ts`
- Modify: `web/src/views/config/system-users/index.vue`

- [x] **Step 1: 调整类型定义和缓存结构**

把前端用户信息和系统用户请求/响应中的 `roles` 改为 `roleCode`，保留 `permissions` 数组。

- [x] **Step 2: 调整系统用户页面交互**

改动包括：
- 角色选择从多选切为单选
- 默认角色逻辑改为返回单个 `common`
- 列表展示单角色
- 新增/编辑表单校验改为必须填写 `roleCode`

- [x] **Step 3: 运行前端类型检查**

Run:
```powershell
pnpm --dir web typecheck
```

Expected: TypeScript 类型检查通过。

### Task 5: 全量回归并收尾

**Files:**
- Modify: `openspec/changes/update-single-role-enforcement/tasks.md`

- [x] **Step 1: 运行后端与 OpenSpec 验证**

Run:
```powershell
openspec validate update-single-role-enforcement --strict
dotnet test tests\AcceptanceSpecSystem.Api.Tests\AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~SystemUsers|FullyQualifiedName~Auth"
dotnet test tests\AcceptanceSpecSystem.Data.Tests\AcceptanceSpecSystem.Data.Tests.csproj -c Release --filter "FullyQualifiedName~AuthUserRoleSingleRoleMigration"
pnpm --dir web typecheck
```

Expected: 全部通过。

- [x] **Step 2: 更新 OpenSpec 任务状态**

把 `openspec/changes/update-single-role-enforcement/tasks.md` 全部改为已完成。
