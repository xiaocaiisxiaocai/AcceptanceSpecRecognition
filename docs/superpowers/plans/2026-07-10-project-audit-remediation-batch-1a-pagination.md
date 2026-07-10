# Project Audit Remediation Batch 1A Pagination Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让智能填充、匹配配置和数据导入完整加载客户、制程、机型选项，并对异常分页契约明确失败。

**Architecture:** 在前端提供无 UI 状态的通用全分页加载器，顺序遍历后端最多 200 条/页的分页契约，统一处理取消、去重、稳定顺序和最大页数。三个业务入口只管理 loading、错误提示和状态写入，列表 API 仅增加可选 `AbortSignal` 透传。

**Tech Stack:** Vue 3、TypeScript、Axios、Vitest、Node test、ASP.NET Core 8、xUnit、SQLite 测试宿主。

---

## 文件边界

**创建：**

- `web/src/utils/paged-options.ts`：通用全分页加载器。
- `web/src/utils/paged-options.test.ts`：加载器行为单元测试。
- `web/tests/master-data-options-pagination.test.ts`：三个页面接入源码守卫。
- `tests/AcceptanceSpecSystem.Api.Tests/MasterDataPaginationContractTests.cs`：客户、制程、机型 251 条分页契约测试。

**修改：**

- `web/src/api/customer.ts`：导出分页列表请求配置并透传 `signal`。
- `web/src/api/process.ts`：透传分页请求配置。
- `web/src/api/machine-model.ts`：透传分页请求配置。
- `web/src/views/smart-fill/index.vue`：范围选项统一全分页加载并取消旧请求。
- `web/src/views/smart-fill/components/MatchConfig.vue`：三个主数据选项统一全分页加载。
- `web/src/views/data-import/composables/useDataImportTarget.ts`：三个导入目标选项统一全分页加载。

## Task 1：建立全分页加载器失败测试

**Files:**

- Create: `web/src/utils/paged-options.test.ts`
- Create: `web/src/utils/paged-options.ts`

- [x] **Step 1：编写 251 条跨页失败测试**

测试回调按 `pageSize = 200` 返回 200 条和 51 条，断言请求页为 `[1, 2]`、结果长度为 251、ID 顺序为 1 到 251。先从尚不存在的 `loadAllPagedItems` 导入，使测试红灯。

同时增加以下用例：

- 重复 ID 保留第一次出现位置。
- `{ total: 0, totalPages: 0, items: [] }` 返回空集合。
- 非首页空页、响应页码不一致、`totalPages > 1000`、业务 `code != 0` 均拒绝并携带可读消息。
- 已取消的 `AbortSignal` 不调用请求回调并抛出取消异常。

- [x] **Step 2：运行测试确认失败**

Run:

```powershell
pnpm --dir web test:vitest -- src/utils/paged-options.test.ts
```

Expected: FAIL，原因是 `paged-options.ts` 或 `loadAllPagedItems` 不存在。

- [x] **Step 3：实现最小加载器**

实现公开契约：

```typescript
type LoadAllPagedItemsOptions<T, TKey> = {
  getKey: (item: T) => TKey;
  signal?: AbortSignal;
  pageSize?: number;
  maxPages?: number;
};

export async function loadAllPagedItems<T, TKey>(
  fetchPage: (
    page: number,
    pageSize: number,
    signal?: AbortSignal
  ) => Promise<ApiResponse<PagedData<T>>>,
  options: LoadAllPagedItemsOptions<T, TKey>
): Promise<T[]>;
```

实现要求：默认 `pageSize = 200`、`maxPages = 1000`；每页前调用 `signal?.throwIfAborted()`；业务码非 0 直接抛错；合法空集合只接受 total/totalPages/items 同时为空；用 `Map<TKey, T>` 保留首次出现顺序；任何异常分页元数据不得返回部分结果。

- [x] **Step 4：运行加载器测试确认通过**

Run: Step 2 命令。

Expected: 全部 PASS。

## Task 2：验证后端主数据分页契约

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/MasterDataPaginationContractTests.cs`

- [x] **Step 1：编写 251 条 API 契约测试**

使用 `ApiWebApplicationFactory`，通过作用域中的 `AppDbContext` 分别写入带唯一前缀的 251 个 `Customer`、`Process`、`MachineModel`。每个端点使用该前缀作为 keyword 请求：

```text
GET /api/customers?page=1&pageSize=200&keyword=<prefix>
GET /api/customers?page=2&pageSize=200&keyword=<prefix>
GET /api/processes?page=1&pageSize=200&keyword=<prefix>
GET /api/processes?page=2&pageSize=200&keyword=<prefix>
GET /api/machine-models?page=1&pageSize=200&keyword=<prefix>
GET /api/machine-models?page=2&pageSize=200&keyword=<prefix>
```

对每类断言第一页 200 条、第二页 51 条、`total = 251`、`totalPages = 2`、`pageSize = 200`、两页 ID 无重复。

- [x] **Step 2：运行 API 契约测试**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~MasterDataPaginationContractTests
```

Expected: PASS，证明前端应按 200 条分页遍历，而不是继续传入 1000。

## Task 3：透传取消信号并接入三个入口

**Files:**

- Modify: `web/src/api/customer.ts`
- Modify: `web/src/api/process.ts`
- Modify: `web/src/api/machine-model.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/views/data-import/composables/useDataImportTarget.ts`
- Create: `web/tests/master-data-options-pagination.test.ts`

- [x] **Step 1：编写页面接入失败守卫**

Node test 读取三个业务入口，断言：

- 均导入并调用 `loadAllPagedItems`。
- 不再出现主数据列表的 `pageSize: 1000` 或客户 `pageSize: 100` 单页加载。
- 智能填充和 MatchConfig 在销毁时取消请求；数据导入 composable 使用 `onScopeDispose` 取消请求。

- [x] **Step 2：运行接入守卫确认失败**

Run:

```powershell
pnpm --dir web test:node -- tests/master-data-options-pagination.test.ts
```

Expected: FAIL，当前三个入口仍直接请求第一页。

- [x] **Step 3：扩展列表 API 的可选配置**

在 `customer.ts` 导出 `PagedListRequestOptions`，三个 API 使用一致签名：

```typescript
export const getCustomerList = (
  params?: PagedRequest,
  options?: PagedListRequestOptions
) => http.request<ApiResponse<PagedData<Customer>>>("get", baseUrl, {
  params,
  signal: options?.signal
});
```

Process 和 MachineModel 保持相同模式，不改变已有单参数调用。

- [x] **Step 4：替换三个页面的主数据加载**

统一调用模式：

```typescript
loadAllPagedItems(
  (page, pageSize, signal) =>
    getCustomerList({ page, pageSize }, { signal }),
  { getKey: item => item.id, signal: controller.signal }
);
```

每次新加载先取消同类旧 controller；请求取消不显示加载失败；其他错误通过 `getRequestErrorMessage` 显示。只有 controller 仍为当前请求时才赋值和关闭 loading，避免旧请求覆盖新状态。

Smart Fill 的三个并行请求共用一次范围加载 controller；MatchConfig 和 data import 对客户、制程、机型分别管理 controller。销毁或 scope dispose 时统一 abort。

- [x] **Step 5：运行前端定向测试和类型检查**

Run:

```powershell
pnpm --dir web test:vitest -- src/utils/paged-options.test.ts
pnpm --dir web test:node -- tests/master-data-options-pagination.test.ts
pnpm --dir web typecheck
```

Expected: 全部 PASS。

- [x] **Step 6：运行 1A 回归**

Run:

```powershell
pnpm --dir web test
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~MasterDataPaginationContractTests
```

Expected: 全部 PASS。

- [x] **Step 7：提交 1A**

```powershell
git add web/src/utils/paged-options.ts web/src/utils/paged-options.test.ts web/tests/master-data-options-pagination.test.ts web/src/api/customer.ts web/src/api/process.ts web/src/api/machine-model.ts web/src/views/smart-fill/index.vue web/src/views/smart-fill/components/MatchConfig.vue web/src/views/data-import/composables/useDataImportTarget.ts tests/AcceptanceSpecSystem.Api.Tests/MasterDataPaginationContractTests.cs docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1a-pagination.md
git commit -m "fix: 完整加载主数据分页选项"
```
