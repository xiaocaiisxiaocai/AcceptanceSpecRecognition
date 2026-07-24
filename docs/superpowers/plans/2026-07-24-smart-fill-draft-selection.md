# Smart Fill Draft Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让智能填充中补齐必填列的范围草稿立即成为可手动勾选的 Sheet，并移除已失效的缺列提示。

**Architecture:** 在 `smartFill.smartRecognition.ts` 增加纯函数，将单个确认草稿转换并合并为 `BatchTableConfigItem`；页面的 `draft-change` 事件调用该函数。确认卡仅在展示层按当前区域映射过滤陈旧缺列问题，后端确认流程保持不变。

**Tech Stack:** Vue 3、TypeScript、Vitest、Node test runner

## Global Constraints

- 这是恢复既有行为的 bugfix，不创建 OpenSpec proposal。
- 只运行相关 Vitest、Node 定向测试和 TypeScript 类型检查。
- 不提交或推送 Git。

---

### Task 1: 草稿配置同步

**Files:**
- Modify: `web/src/views/smart-fill/smartFill.smartRecognition.ts`
- Modify: `web/src/views/smart-fill/smartFill.smartRecognition.test.ts`
- Modify: `web/src/views/smart-fill/index.vue`

**Interfaces:**
- Consumes: `SmartConfigConfirmRequest`、`applySmartConfigConfirmRequestToTable`
- Produces: `syncSmartFillDraftConfig(options): BatchTableConfigItem[]`

- [x] **Step 1: Write the failing test**

测试缺验收列的识别表在应用有效草稿后生成一条未选中的配置，并验证撤销草稿会移除临时配置。

- [x] **Step 2: Run test to verify it fails**

Run: `pnpm exec vitest run src/views/smart-fill/smartFill.smartRecognition.test.ts`

Expected: FAIL，因为 `syncSmartFillDraftConfig` 尚不存在。

- [x] **Step 3: Write minimal implementation**

实现纯函数：应用草稿、调用现有配置构建器、保留旧选择状态、按表索引合并；页面在 `handleSmartStructureDraftChange` 中调用。

- [x] **Step 4: Run test to verify it passes**

Run: `pnpm exec vitest run src/views/smart-fill/smartFill.smartRecognition.test.ts`

Expected: PASS。

### Task 2: 陈旧缺列提示过滤

**Files:**
- Modify: `web/src/views/shared/SmartStructureConfirmCard.vue`
- Modify: `web/tests/data-import-confirm-layout.test.ts`

**Interfaces:**
- Consumes: 当前 `activeRegions`
- Produces: 只包含仍未解决问题的 `visibleIssues`

- [x] **Step 1: Write the failing test**

增加可执行的组件逻辑测试或最小纯函数测试，验证当前区域已有项目列和验收列时，不再返回对应缺列问题，而备注列问题仍保留。

- [x] **Step 2: Run test to verify it fails**

Run: `node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts`

Expected: 相关新断言 FAIL。

- [x] **Step 3: Write minimal implementation**

根据当前区域的必填列映射过滤三个 `Missing*Column` 问题，不更改其他问题。

- [x] **Step 4: Run focused verification**

Run:

```powershell
pnpm exec vitest run src/views/smart-fill/smartFill.smartRecognition.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-confirm-layout.test.ts
pnpm typecheck
git diff --check
```

Expected: 新增回归测试和受影响检查通过；若旧测试存在与本修复无关的基线失败，单独列明。
