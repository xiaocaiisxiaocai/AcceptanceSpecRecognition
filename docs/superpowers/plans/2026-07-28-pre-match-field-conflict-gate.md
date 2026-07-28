# 字段冲突前置确认 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 数据导入和智能填充在结构识别发现字段候选冲突后立即要求用户明确选列，并在确认前禁止生成正式预览或进入业务匹配。

**Architecture:** 保留后端结构识别返回的推荐列和候选列，只把推荐列视为临时建议。共享冲突模块负责识别未解决冲突和应用人工选择；两个页面在各自的结构识别完成回调中调用同一门禁，确认后才构造下游配置。

**Tech Stack:** Vue 3、TypeScript、Element Plus、Vitest、Node Test、OpenSpec

## Global Constraints

- 系统推荐只作视觉提示，不自动成为用户选择。
- “暂不处理”不得学习配置、生成正式预览、导入数据或执行智能填充匹配。
- 未勾选 Sheet 的冲突不得阻塞当前流程；后续勾选时必须重新检查。
- 不修改后端 API、数据库结构及历史数据。

---

### Task 1: 共享字段冲突门禁

**Files:**
- Modify: `web/src/views/shared/smart-structure-field-conflicts.ts`
- Modify: `web/src/views/shared/smart-structure-field-conflicts.test.ts`
- Modify: `web/src/views/shared/SmartStructureFieldConflictDialog.vue`

**Interfaces:**
- Consumes: `SmartConfigRecognizedTable[]` 与已选 Sheet 索引。
- Produces: 未解决冲突集合、空的显式选择状态、人工选择后的识别表。

- [x] **Step 1: Write the failing test**

  新增测试，断言弹框初始化选择为空，推荐列仍可单独查询；未人工选择时解决数量为零。

- [x] **Step 2: Run test to verify it fails**

  Run: `pnpm test:vitest -- src/views/shared/smart-structure-field-conflicts.test.ts`
  Expected: FAIL，当前实现仍把推荐列作为默认选择。

- [x] **Step 3: Write minimal implementation**

  增加创建空选择状态的共享函数，并让弹框打开时只清空旧选择，不再写入推荐列。

- [x] **Step 4: Run test to verify it passes**

  Run: `pnpm test:vitest -- src/views/shared/smart-structure-field-conflicts.test.ts`
  Expected: PASS

### Task 2: 数据导入在预览前处理冲突

**Files:**
- Modify: `web/src/views/data-import/index.vue`
- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`
- Modify: `web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts`
- Modify: `web/src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts`
- Modify: `web/tests/data-import-confirm-layout.test.ts`

**Interfaces:**
- Consumes: 结构识别原始结果和默认已选 Sheet。
- Produces: `resolveInitialFieldConflicts(tables, selectedIndexes)`；返回已完成人工选列的表，取消时返回 `null`。

- [x] **Step 1: Write the failing test**

  新增编排测试，断言冲突解决回调发生在 `applySmartRecognizedTables` 和预览加载之前；取消时不加载预览。

- [x] **Step 2: Run test to verify it fails**

  Run: `pnpm test:vitest -- src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts`
  Expected: FAIL，当前识别后立即生成预览。

- [x] **Step 3: Write minimal implementation**

  给数据导入识别 composable 注入异步冲突解决器；识别后先等待人工结果，确认后替换识别表并生成预览，取消时停留在结构确认步骤。

- [x] **Step 4: Run test to verify it passes**

  Run: `pnpm test:vitest -- src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts`
  Expected: PASS

### Task 3: 智能填充在配置与匹配前处理冲突

**Files:**
- Modify: `web/src/views/smart-fill/index.vue`
- Modify: `web/tests/smart-fill-recognition-selection.test.ts`

**Interfaces:**
- Consumes: 结构识别结果、由识别结果生成的默认 Sheet 选择。
- Produces: 前置冲突确认状态；确认后重建 `batchTableConfigs`，取消后停留在结构确认页。

- [x] **Step 1: Write the failing test**

  新增流程契约测试，断言识别完成后立即收集冲突并在进入结构确认页时打开弹框，且匹配预览只能在冲突解决后触发。

- [x] **Step 2: Run test to verify it fails**

  Run: `pnpm test:node -- --test-name-pattern="字段候选冲突"`
  Expected: FAIL，当前只在确认学习按钮处检查冲突。

- [x] **Step 3: Write minimal implementation**

  为弹框增加“识别后前置确认”和“后续选择兜底确认”两种内部上下文；前置确认后重建最终配置，后续兜底保持原批量确认行为。

- [x] **Step 4: Run test to verify it passes**

  Run: `pnpm test:node -- --test-name-pattern="字段候选冲突"`
  Expected: PASS

### Task 4: 定向验证

**Files:**
- Modify: `openspec/changes/combine-smart-fill-sheet-confirm/**`
- Modify: `openspec/changes/extend-field-conflict-confirm-to-data-import/**`

**Interfaces:**
- Consumes: 前三项的实现和测试结果。
- Produces: 与真实行为一致的 OpenSpec 变更、类型检查和定向回归证据。

- [x] **Step 1: Validate affected OpenSpec changes**

  Run: `openspec validate combine-smart-fill-sheet-confirm --strict`
  Run: `openspec validate extend-field-conflict-confirm-to-data-import --strict`

- [x] **Step 2: Run focused frontend tests**

  Run: `pnpm test:vitest -- src/views/shared/smart-structure-field-conflicts.test.ts src/views/data-import/composables/useDataImportSmartStructureRecognition.test.ts`
  Run: `pnpm test:node -- --test-name-pattern="字段候选冲突|数据导入应在"`

- [x] **Step 3: Run type checking**

  Run: `pnpm typecheck`

- [x] **Step 4: Review the diff**

  检查本任务文件的 `git diff`，确认未混入现有工作区的组织、权限、审计或验收规格改动。
