# Acceptance Spec Query Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 稳定验收规格并发查询结果、保留每页 500 条能力，并用组合排序索引降低分组分页排序成本。

**Architecture:** 前端在组件内部以递增请求序号约束异步结果，不修改公共 HTTP 封装；数据库通过 EF Core 模型和 Migration 将原三列分组索引替换为包含排序字段的五列索引，同时让仓储使用 `ImportedAt, Id` 稳定倒序。迁移应用前使用 `mysqldump` 备份当前本地库。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Node Test Runner、ASP.NET Core 8、EF Core 8、Pomelo MySQL、MySQL 5.7。

## Global Constraints

- 验收规格分页选项必须为 `100 / 200 / 500`，默认值保持 100。
- 只有最新列表请求可以更新表格、总数和加载状态。
- 保持关键词搜索字段、模糊匹配语义和 API 契约不变。
- 数据库迁移前必须备份，迁移不得修改业务记录。
- 小型定向优化只运行相关测试、类型检查、代码规范检查和迁移验证。
- 不提交、不切换分支、不推送 Git。

---

### Task 1: Frontend latest-request protection and page sizes

**Files:**
- Modify: `web/tests/spec-global-search.test.ts`
- Modify: `web/src/views/base-data/specs/components/SpecTable.vue`

**Interfaces:**
- Consumes: existing `getSpecList(params)` API.
- Produces: component-local `latestLoadRequestId: number`; pagination sizes `[100, 200, 500]`.

- [ ] **Step 1: Write failing source-contract tests**

Add assertions requiring a monotonically increasing request id, stale-response guards around result writes and `loading`, and the exact page-size list:

```ts
assert.match(specTableSource, /let latestLoadRequestId = 0/);
assert.match(specTableSource, /const requestId = \+\+latestLoadRequestId/);
assert.match(specTableSource, /if \(requestId !== latestLoadRequestId\) return/);
assert.match(specTableSource, /if \(requestId === latestLoadRequestId\)[\s\S]*loading\.value = false/);
assert.match(specTableSource, /:page-sizes="\[100, 200, 500\]"/);
assert.doesNotMatch(specTableSource, /:page-sizes="[^"]*1000/);
```

- [ ] **Step 2: Run the focused test and confirm RED**

Run:

```powershell
cd web
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/spec-global-search.test.ts
```

Expected: the new latest-request and page-size assertions fail because the component still has no request id and exposes 1,000.

- [ ] **Step 3: Implement the minimal frontend behavior**

In `SpecTable.vue`, declare `let latestLoadRequestId = 0;`. At the beginning of `loadData`, capture `const requestId = ++latestLoadRequestId`. Guard successful response writes and error messages against stale requests. In `finally`, clear loading only when `requestId === latestLoadRequestId`. Change pagination sizes to `[100, 200, 500]`.

- [ ] **Step 4: Verify frontend GREEN**

Run:

```powershell
cd web
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/spec-global-search.test.ts
pnpm exec vue-tsc --noEmit
pnpm exec eslint --max-warnings 0 src/views/base-data/specs/components/SpecTable.vue tests/spec-global-search.test.ts
pnpm exec prettier --check src/views/base-data/specs/components/SpecTable.vue tests/spec-global-search.test.ts
```

Expected: focused tests, type check, lint and formatting all pass.

### Task 2: Stable repository ordering and composite index model

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecRepositoryQueryTests.cs`
- Create: `tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecIndexModelTests.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/AcceptanceSpecRepository.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs`

**Interfaces:**
- Consumes: `AcceptanceSpecRepository.GetPagedWithFilterAsync`.
- Produces: stable ordering `ImportedAt DESC, Id DESC`; model index named `IX_AcceptanceSpecs_CustomerId_ProcessId_MachineModelId_ImportedAt_Id`.

- [ ] **Step 1: Write failing stable-order and model-index tests**

Add a repository case with two rows sharing `ImportedAt` and assert descending `Id`. Add a model metadata test that locates the `AcceptanceSpec` entity and asserts the five index properties in exact order while asserting the old three-column index is absent.

- [ ] **Step 2: Run Data tests and confirm RED**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj -c Debug --filter "FullyQualifiedName~AcceptanceSpecRepositoryQueryTests|FullyQualifiedName~AcceptanceSpecIndexModelTests"
```

Expected: ordering or model index assertions fail before production changes.

- [ ] **Step 3: Implement stable ordering and model index**

Change paged repository ordering to:

```csharp
.OrderByDescending(spec => spec.ImportedAt)
.ThenByDescending(spec => spec.Id)
```

Replace the three-column model index with:

```csharp
entity.HasIndex(e => new
{
    e.CustomerId,
    e.ProcessId,
    e.MachineModelId,
    e.ImportedAt,
    e.Id
});
```

- [ ] **Step 4: Verify Data tests GREEN**

Re-run the filtered Data tests and require zero failures.

### Task 3: Generate and inspect EF Core migration

**Files:**
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_OptimizeAcceptanceSpecGroupPagingIndex.cs`
- Create: `src/AcceptanceSpecSystem.Data/Migrations/<timestamp>_OptimizeAcceptanceSpecGroupPagingIndex.Designer.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Migrations/AppDbContextModelSnapshot.cs`

**Interfaces:**
- Produces: reversible migration replacing the old three-column index with the five-column index.

- [ ] **Step 1: Generate migration**

Run:

```powershell
dotnet ef migrations add OptimizeAcceptanceSpecGroupPagingIndex `
  --project src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj `
  --startup-project src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj `
  --context AppDbContext
```

- [ ] **Step 2: Inspect migration scope**

Confirm `Up` only drops the old composite index and creates the five-column index; confirm `Down` reverses those two operations. Check `git diff --check`.

- [ ] **Step 3: Verify migration metadata**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Data.Tests/AcceptanceSpecSystem.Data.Tests.csproj -c Debug --filter "FullyQualifiedName~MigrationMetadataTests|FullyQualifiedName~AcceptanceSpecIndexModelTests|FullyQualifiedName~AcceptanceSpecRepositoryQueryTests"
```

Expected: all targeted Data tests pass.

### Task 4: Backup and apply the local database migration

**Files:**
- Create runtime backup outside source directories under `backups/manual/`.

**Interfaces:**
- Consumes: sanitized connection values parsed from `appsettings.Development.json`.
- Produces: non-empty SQL dump; applied EF migration; unchanged `AcceptanceSpecs` row count.

- [ ] **Step 1: Capture pre-migration evidence**

Read and record exact `COUNT(*)`, current migration id and current composite index columns using the MySQL client without printing credentials.

- [ ] **Step 2: Create and validate backup**

Create `backups/manual/acceptance-spec-query-index-<timestamp>.sql` with `mysqldump`, using process-scoped `MYSQL_PWD`. Verify the file exists, is non-empty, and contains the `AcceptanceSpecs` table definition.

- [ ] **Step 3: Apply migration**

Run:

```powershell
dotnet ef database update `
  --project src/AcceptanceSpecSystem.Data/AcceptanceSpecSystem.Data.csproj `
  --startup-project src/AcceptanceSpecSystem.Api/AcceptanceSpecSystem.Api.csproj `
  --context AppDbContext
```

- [ ] **Step 4: Verify database state**

Confirm:

- `COUNT(*)` equals the pre-migration count.
- `__EFMigrationsHistory` contains `OptimizeAcceptanceSpecGroupPagingIndex`.
- `SHOW INDEX` reports the five columns in order.
- the old three-column index name is absent.
- typical group pagination `EXPLAIN` does not report `Using filesort`.

### Task 5: Final targeted verification and documentation status

**Files:**
- Modify: `openspec/changes/optimize-acceptance-spec-query/tasks.md`

**Interfaces:**
- Produces: checked OpenSpec task list matching verified work.

- [ ] **Step 1: Run final scoped verification**

Run frontend focused tests, Vue type check, ESLint, Prettier, targeted Data tests, `openspec validate optimize-acceptance-spec-query --strict`, `git diff --check`, and HTTP health checks for ports 8849 and 5291.

- [ ] **Step 2: Review changed-file scope**

Run `git status --short` and `git diff --stat`; distinguish earlier smart-fill/UI changes from files added by this optimization.

- [ ] **Step 3: Update OpenSpec checklist**

Mark every completed item in `openspec/changes/optimize-acceptance-spec-query/tasks.md` as `[x]` only after its evidence has passed.
