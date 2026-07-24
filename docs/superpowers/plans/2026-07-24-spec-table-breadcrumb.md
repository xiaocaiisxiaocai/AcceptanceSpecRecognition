# Spec Table Breadcrumb Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 精简验收规格列表的来源列，并用面包屑展示当前数据范围。

**Architecture:** 仅修改 `SpecTable.vue` 的展示层；现有查询参数和来源数据继续保留。使用计算属性生成面包屑数组，模板通过 Element Plus `el-breadcrumb` 渲染。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Node test runner

## Global Constraints

- 保留全局搜索业务行为，只删除表格中的来源列。
- 不修改 API、数据模型和权限判断。
- 不提交或推送 Git。

---

### Task 1: 面包屑与表格列

**Files:**
- Modify: `web/src/views/base-data/specs/components/SpecTable.vue`
- Modify: `web/tests/spec-global-search.test.ts`

**Interfaces:**
- Consumes: `queryParams.globalSearch`、`customerName`、`machineModelName`、`processName`
- Produces: `scopeBreadcrumbItems: string[]`

- [x] **Step 1: Write the failing test**

更新全局搜索测试，要求来源列不再渲染，并要求模板存在动态面包屑。

- [x] **Step 2: Verify the test fails**

Run: `node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/spec-global-search.test.ts`

Expected: FAIL，因为当前仍渲染四个来源列且使用标签。

- [x] **Step 3: Implement the minimal UI change**

增加面包屑计算属性，替换顶部标签，删除四个 `el-table-column`，不删除详情弹窗和查询所需字段。

- [x] **Step 4: Verify the focused change**

Run:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/spec-global-search.test.ts
pnpm typecheck
pnpm exec eslint --max-warnings 0 src/views/base-data/specs/components/SpecTable.vue
pnpm exec prettier --check src/views/base-data/specs/components/SpecTable.vue
```

Expected: 全部通过。
