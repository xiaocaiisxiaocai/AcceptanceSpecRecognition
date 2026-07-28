# 层级组织管理实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 恢复公司、事业部、部门、课别的完整组织树管理，同时保持每个用户只能归属一个组织节点，并让角色数据范围与组织层级一致。

**Architecture:** API 控制器继续只负责公司上下文和 HTTP 适配，组织不变量与事务写入集中在 `OrgUnitAppService`。用户归属和角色范围分别由现有 `SystemUserAppService`、`AuthRoleAppService` 校验，运行时范围继续复用 `AuthDataScopeService` 的节点与路径展开逻辑。

**Tech Stack:** ASP.NET Core 8、EF Core 8、MySQL 8、xUnit、Vue 3、TypeScript、Element Plus

## Global Constraints

- 每个公司只有一个 `Company` 根节点。
- 组织类型固定为 `Company < Division < Department < Section`，允许向下跳级。
- 每个用户只能归属当前公司内一个有效组织节点。
- 不支持移动已有节点或修改已有节点类型。
- 不级联删除组织关联数据，不清空或重建历史组织。
- 所有组织写操作必须通过现有权限体系并写入审计日志。
- 先观察失败测试，再写最小实现。

---

### Task 1: 恢复组织树查询与创建

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/OrgUnitsTests.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/OrgUnitAppService.cs`

**Interfaces:**
- Produces: `POST /api/org-units`
- Produces: `IOrgUnitAppService.CreateAsync(int, CreateOrgUnitRequest, CancellationToken)`
- Produces: 完整 `GetTreeAsync` 和 `GetFlatAsync`

- [ ] **Step 1: 写组织树与创建的失败集成测试**

在 `OrgUnitsTests` 中用字面量断言：

```csharp
[Fact]
public async Task CreateChild_WithSkippedValidLevel_ShouldPersistPathAndAppearInTree()
{
    var rootId = await GetRootOrgUnitIdAsync();
    var response = await _client.PostAsync("/api/org-units", ApiClientJson.ToJsonContent(new
    {
        parentId = rootId,
        unitType = (int)OrgUnitType.Department,
        code = $"DEP-{Guid.NewGuid():N}"[..18],
        name = "品质部",
        sort = 10,
        isActive = true
    }));
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var created = await response.ReadAsAsync<ApiResponse<JsonElement>>();
    created.Data.GetProperty("parentId").GetInt32().Should().Be(rootId);
    created.Data.GetProperty("depth").GetInt32().Should().Be(1);
    created.Data.GetProperty("path").GetString().Should().MatchRegex($@"^/{rootId}/\d+/$");
}
```

同时将 `GetTree_WhenDatabaseContainsChildOrgUnits_ShouldOnlyReturnRootNode` 改为断言子节点出现在 `children`，并增加平铺列表包含全部节点的测试。

- [ ] **Step 2: 运行测试并确认按预期失败**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~OrgUnitsTests"
```

Expected: 创建接口返回 404，树接口缺少子节点。

- [ ] **Step 3: 恢复控制器创建端点**

增加：

```csharp
[HttpPost]
[AuditOperation("create", "org-unit")]
public async Task<ActionResult<ApiResponse<OrgUnitDto>>> Create(
    [FromBody] CreateOrgUnitRequest request,
    CancellationToken cancellationToken = default)
```

控制器只解析 `companyId`、调用应用服务并翻译 `ApplicationServiceException`。

- [ ] **Step 4: 实现树构建和平铺稳定排序**

`GetTreeAsync` 一次读取当前公司全部节点，按 `Depth`、`Sort`、`Id` 排序后通过字典组装 `Children`；`GetFlatAsync` 返回相同稳定顺序。孤立节点不得静默挂到其他公司根节点。

- [ ] **Step 5: 实现创建不变量**

`CreateAsync` 必须校验：

```csharp
parent.CompanyId == companyId
parent.IsActive
request.UnitType > parent.UnitType
parent.UnitType != OrgUnitType.Section
request.UnitType != OrgUnitType.Company
```

编码按公司唯一并大写；首次保存取得 ID 后设置：

```csharp
entity.Depth = parent.Depth + 1;
entity.Path = $"{parent.Path}{entity.Id}/";
```

- [ ] **Step 6: 运行组织测试确认 GREEN**

Run: Task 1 Step 2 的命令。
Expected: 全部 `OrgUnitsTests` 通过。

### Task 2: 恢复下级编辑、停用保护与安全删除

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/OrgUnitsTests.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/OrgUnitAppService.cs`

**Interfaces:**
- Produces: `DELETE /api/org-units/{id}`
- Consumes: Task 1 的完整组织查询与创建规则

- [ ] **Step 1: 写编辑与删除保护失败测试**

覆盖：

- 下级节点可修改名称、编码、排序和状态。
- 根节点不能停用或删除。
- 有有效子节点或有效用户归属时不能停用。
- 有子节点、用户、角色范围、`AcceptanceSpec.OwnerOrgUnitId` 或 `WordFile.OwnerOrgUnitId` 引用时不能删除。
- 无引用叶子节点可删除。
- [ ] **Step 2: 运行测试确认当前实现失败**

Run: Task 1 Step 2 的命令。
Expected: 下级更新返回 400，删除返回 405。

- [ ] **Step 3: 放开下级节点普通字段编辑**

移除“只允许根节点更新”限制，但不向 `UpdateOrgUnitRequest` 增加 `ParentId` 或 `UnitType`。根节点继续拒绝停用。

- [ ] **Step 4: 增加停用前置校验**

当 `request.IsActive == false` 且原节点为启用状态时，查询有效直接子节点和有效用户组织关系；任一存在则返回具体 400 错误。

- [ ] **Step 5: 恢复删除端点和引用检查**

控制器增加 `[HttpDelete("{id:int}")]` 与 `[AuditOperation("delete", "org-unit")]`。应用服务依次拒绝根节点、子节点、用户归属、角色范围节点、规格和文件引用，最后才删除实体并保存。

- [ ] **Step 6: 运行组织测试确认 GREEN**

Run: Task 1 Step 2 的命令。
Expected: 全部组织测试通过，删除失败原因可区分。

### Task 3: 允许用户归属任意有效组织节点

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/SystemUsersTests.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/SystemUserAppService.cs`

**Interfaces:**
- Produces: `ResolveOrgUnitIdAsync` 接受当前公司内任意有效节点
- Preserves: 单个 `orgUnitId` 请求与 `AuthUserOrgUnits.UserId` 唯一约束

- [ ] **Step 1: 将旧拒绝测试改为下级组织成功测试**

把 `Create_WithNonRootOrgUnit_ShouldReturnBadRequest` 改为创建用户后断言返回的 `orgUnitId` 等于所选下级节点，并增加其他公司、停用和不存在节点的拒绝测试。

- [ ] **Step 2: 运行用户测试确认失败**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~SystemUsersTests"
```

Expected: 合法下级组织仍被“只允许根组织”校验拒绝。

- [ ] **Step 3: 修改组织归属解析**

`ResolveOrgUnitIdAsync` 查询：

```csharp
org.Id == orgUnitId &&
org.CompanyId == companyId &&
org.IsActive
```

空值、不存在、其他公司和停用节点返回 `null`，调用方继续产生 400，且不改变现有用户关系。

- [ ] **Step 4: 运行用户测试确认 GREEN**

Run: Task 3 Step 2 的命令。
Expected: 用户测试全部通过。

### Task 4: 恢复角色层级数据范围与权限种子

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthRolesTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/AuthPermissionsTests.cs`
- Modify: `src/AcceptanceSpecSystem.Application/Services/AuthRoleAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/OrgUnitsController.cs`

**Interfaces:**
- Consumes: `AuthRoleDataScopeDto.OrgUnitIds`
- Produces: `OrgNode`/`OrgSubtree` 单节点与 `CustomNodes` 多节点校验
- Produces: `api:org-unit:create`, `api:org-unit:delete`, `btn:org-unit:create`, `btn:org-unit:delete`

- [ ] **Step 1: 写角色范围失败测试**

覆盖下级节点的 `OrgNode`、`OrgSubtree` 保存成功，自定义多个同公司节点成功，其他公司、停用和不存在节点失败且不留下部分关系。

- [ ] **Step 2: 更新权限种子预期并观察失败**

将 `AuthPermissionsTests` 中组织 create/delete 的 `NotContain` 改为 `Contain`，并验证权限类型和 API 方法。

- [ ] **Step 3: 运行角色和权限测试确认失败**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AuthRolesTests|FullyQualifiedName~AuthPermissionsTests"
```

- [ ] **Step 4: 放开角色组织范围校验**

移除根节点和 `CustomNodes` 禁止逻辑。一次查询全部请求节点，要求数量完全匹配且全部满足 `CompanyId == companyId && IsActive`；校验通过后才替换原范围关系。

- [ ] **Step 5: 通过控制器元数据恢复权限种子**

Task 1、2 增加的 create/delete 审计端点将由 `AuthPermissionSeedCatalog` 自动产生 API 和按钮权限；确认种子同步会重新启用历史失活权限。

- [ ] **Step 6: 运行角色、权限和数据范围测试**

Run: Task 4 Step 3 的命令，并增加：

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~AuthDataScopeServiceTests"
```

Expected: 节点和子树范围均通过。

### Task 5: 恢复组织树前端 CRUD

**Files:**
- Modify: `web/tests/layout-density.test.ts`
- Modify: `web/tests/destructive-action-errors.test.ts`
- Modify: `web/src/views/config/org-units/index.vue`
- Reuse: `web/src/api/org-unit.ts`

**Interfaces:**
- Consumes: `getOrgUnitTree`, `createOrgUnit`, `updateOrgUnit`, `deleteOrgUnit`
- Produces: 树形表格、新增/编辑弹窗和删除确认

- [ ] **Step 1: 更新前端契约测试并确认 RED**

断言组织页使用 `treeData`、`:tree-props="{ children: 'children' }"`，存在“新增下级”和“删除”操作、合法类型过滤、权限码及统一破坏性错误处理。

```powershell
cd web
pnpm exec node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/layout-density.test.ts ./tests/destructive-action-errors.test.ts
```

Expected: 当前根节点页面缺少新增和删除操作。

- [ ] **Step 2: 恢复树形展示与类型标签**

树表使用 `row-key="id"`、`default-expand-all` 和 `tree-props`。类型标签固定映射公司、事业部、部门、课别；停用节点保持可见但弱化显示。

- [ ] **Step 3: 实现新增下级弹窗**

点击父节点时根据 `unitTypeOptions.filter(type => type.value > parent.unitType)` 提供可选类型。课别隐藏新增入口；提交前校验编码、名称和合法类型。

- [ ] **Step 4: 实现编辑和删除**

编辑不显示父节点和类型字段。删除使用 `ElMessageBox.confirm`，主动取消与请求错误分别处理，并显示后端具体引用原因。

- [ ] **Step 5: 运行前端契约测试确认 GREEN**

Run: Task 5 Step 1 的命令。
Expected: 定向测试全部通过。

### Task 6: 恢复角色自定义节点范围并验证用户组织选择

**Files:**
- Modify: `web/src/views/config/auth-roles/index.vue`
- Modify: `web/src/views/config/auth-roles/components/RoleFormDialog.vue`
- Modify: `web/src/views/config/auth-roles/roleForm.types.ts`
- Verify: `web/src/views/config/system-users/index.vue`
- Test: 对应现有前端契约测试文件

**Interfaces:**
- Produces: `ScopeType` 包含 `3`（CustomNodes）
- Consumes: 完整 `getOrgUnitFlat()` 结果

- [ ] **Step 1: 写角色自定义节点范围失败测试**

断言范围选项包含“自定义组织”，类型 1/2 为单选，类型 3 为多选且提交全部去重节点 ID。

- [ ] **Step 2: 运行测试确认 RED**

运行包含角色表单契约的 Node/Vitest 定向测试。
Expected: 当前范围选项不含类型 3。

- [ ] **Step 3: 实现范围选择行为**

`ScopeType` 加入 `3`；`ensureScopeNodeSelection` 仅截断类型 1/2；`buildDataScopes` 对类型 3 保留全部去重 ID；表单组件按类型切换单选与多选。

- [ ] **Step 4: 验证系统用户组织单选**

确认系统用户页继续使用单个 `orgUnitId`，但完整平铺列表按深度缩进显示全部有效节点，不引入组织数组字段。

- [ ] **Step 5: 运行相关前端测试和类型检查**

```powershell
cd web
pnpm typecheck
pnpm exec eslint --max-warnings 0 src/views/config/org-units/index.vue src/views/config/auth-roles/index.vue src/views/config/auth-roles/components/RoleFormDialog.vue
pnpm exec stylelint src/views/config/org-units/index.vue src/views/config/auth-roles/index.vue src/views/config/auth-roles/components/RoleFormDialog.vue
```

### Task 7: 数据兼容、运行验证与评审

**Files:**
- Modify: `openspec/changes/restore-hierarchical-organization-management/tasks.md`
- Verify only: local MySQL `OrgUnits` and reference counts

**Interfaces:**
- Validates: 历史组织不重建、不删除
- Validates: API 5291、Web 8849

- [ ] **Step 1: 运行完整受影响后端测试集合**

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~OrgUnitsTests|FullyQualifiedName~SystemUsersTests|FullyQualifiedName~AuthRolesTests|FullyQualifiedName~AuthPermissionsTests|FullyQualifiedName~AuthDataScopeServiceTests"
```

- [ ] **Step 2: 查询本地 MySQL 数据完整性**

只读核对每类组织数量、重复公司编码、孤立父节点、非法路径、用户多组织记录和组织引用；不得更新或清理数据。

- [ ] **Step 3: 重新启动当前项目并执行冒烟流程**

确认监听进程属于当前仓库后，仅重启 5291 API；保留 8849 和 3306。验证：

1. 新增公司直属部门。
2. 新增部门直属课别。
3. 将测试用户分配到部门。
4. 配置角色为部门子树。
5. 验证受引用组织删除被拒绝。
6. 删除专门创建且无引用的测试叶子节点。

- [ ] **Step 4: 运行最终静态检查**

```powershell
git diff --check
openspec validate restore-hierarchical-organization-management --strict
```

- [ ] **Step 5: 自审并更新任务状态**

复核公司边界、删除保护、权限码、审计、历史数据、测试真实性和当前工作区其他修改；完成后逐项将 OpenSpec `tasks.md` 标为 `[x]`。
