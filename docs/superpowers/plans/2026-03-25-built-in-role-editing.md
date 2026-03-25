# 内置角色编辑放开 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 允许直接编辑内置角色 `admin/common`，同时继续禁止删除内置角色。

**Architecture:** 这次改动只收口在角色管理链路本身，不扩散到用户、权限字典或组织模型。后端只放开 `PUT /api/auth-roles/{id}` 的内置角色限制，前端只放开编辑入口与状态控件，删除限制维持原样。

**Tech Stack:** ASP.NET Core 8、EF Core 8、xUnit、Vue 3、TypeScript、Element Plus

---

### Task 1: 更新规格与计划文档

**Files:**
- Create: `openspec/changes/update-built-in-role-editing/proposal.md`
- Create: `openspec/changes/update-built-in-role-editing/tasks.md`
- Create: `openspec/changes/update-built-in-role-editing/specs/user-interface/spec.md`
- Create: `openspec/changes/update-built-in-role-editing/specs/api/spec.md`
- Create: `docs/superpowers/specs/2026-03-25-built-in-role-editing-design.md`

- [ ] **Step 1: 补齐 OpenSpec 变更文档**

写明“内置角色允许编辑、但仍然不可删除”的行为边界。

- [ ] **Step 2: 运行变更校验**

Run: `openspec validate update-built-in-role-editing --strict`
Expected: 校验通过

### Task 2: 先写失败测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthRolesTests.cs`

- [ ] **Step 1: 修改内置角色更新测试为成功预期**

把现有“内置角色更新失败”测试改成“内置角色更新成功并持久化”。

- [ ] **Step 2: 运行单测确认失败**

Run: `dotnet test tests\\AcceptanceSpecSystem.Api.Tests\\AcceptanceSpecSystem.Api.Tests.csproj -c Release -p:UseSharedCompilation=false --filter "FullyQualifiedName~AuthRolesTests"`
Expected: 至少 1 个失败，失败原因为后端仍拒绝编辑内置角色

### Task 3: 实现后端放开编辑

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs`

- [ ] **Step 1: 删除内置角色更新拦截**

保留删除限制，不再拦截 `Update` 中的 `role.IsBuiltIn`。

- [ ] **Step 2: 运行角色测试确认通过**

Run: `dotnet test tests\\AcceptanceSpecSystem.Api.Tests\\AcceptanceSpecSystem.Api.Tests.csproj -c Release -p:UseSharedCompilation=false --filter "FullyQualifiedName~AuthRolesTests"`
Expected: 角色相关测试全部通过

### Task 4: 实现前端放开编辑入口

**Files:**
- Modify: `web/src/views/config/auth-roles/index.vue`

- [ ] **Step 1: 放开编辑按钮与弹窗入口**

移除内置角色编辑拦截和编辑按钮禁用。

- [ ] **Step 2: 放开内置角色状态编辑**

编辑弹窗中的状态开关不再因内置角色而禁用。

- [ ] **Step 3: 保留删除限制**

删除按钮仍然对内置角色禁用。

### Task 5: 完整验证

**Files:**
- Test: `tests/AcceptanceSpecSystem.Api.Tests/AuthRolesTests.cs`
- Test: `web/src/views/config/auth-roles/index.vue`

- [ ] **Step 1: 运行 OpenSpec 校验**

Run: `openspec validate update-built-in-role-editing --strict`
Expected: 校验通过

- [ ] **Step 2: 运行角色 API 测试**

Run: `dotnet test tests\\AcceptanceSpecSystem.Api.Tests\\AcceptanceSpecSystem.Api.Tests.csproj -c Release -p:UseSharedCompilation=false --filter "FullyQualifiedName~AuthRolesTests"`
Expected: 全部通过

- [ ] **Step 3: 运行前端类型检查**

Run: `pnpm --dir web typecheck`
Expected: 通过

- [ ] **Step 4: 运行前端构建**

Run: `pnpm --dir web build`
Expected: 通过
