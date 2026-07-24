# Smart Fill Hidden Runtime Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove user-facing AI service and empty-row controls from smart fill while forcing every smart-fill preview and execution request to filter empty rows.

**Architecture:** Keep runtime Embedding and LLM selection inside `MatchConfig.vue`, because preview blocking and advanced LLM controls still consume service availability. Remove only the three basic form entries. Normalize `filterEmptySourceRows` at both request boundaries so stale global or table-level `false` values cannot escape to the backend.

**Tech Stack:** Vue 3 SFC, TypeScript, Vitest, Node test runner, Element Plus

## Global Constraints

- Do not modify backend APIs, AI service configuration, runtime service detection, permissions, matching algorithms, or preview blocking.
- Remove the controls from both global and per-table smart-fill configuration.
- Every smart-fill preview and execution request must send `filterEmptySourceRows: true` in both the global config and every table config.
- Preserve compatibility fields in existing TypeScript API types.
- Do not commit or push Git.

---

### Task 1: Hide runtime-owned settings

**Files:**
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/views/smart-fill/components/BatchTableConfig.vue`
- Modify: `web/tests/smart-fill-default-config.test.ts`

**Interfaces:**
- Consumes: existing `MatchConfig` model and runtime AI selection state.
- Produces: a simplified matching form; `getServiceStatus()` remains unchanged for preview guards.

- [ ] **Step 1: Write the failing UI regression test**

Add a test that loads both SFC sources and asserts:

```ts
assert.doesNotMatch(matchConfigSource, /<el-form-item label="Embedding 服务">/);
assert.doesNotMatch(matchConfigSource, /<el-form-item label="LLM 服务">/);
assert.doesNotMatch(matchConfigSource, /<el-form-item label="过滤空行">/);
assert.doesNotMatch(batchTableConfigSource, /<el-form-item label="过滤空行">/);
assert.match(matchConfigSource, /loadRuntimeAiSelectionsSettled/);
assert.match(matchConfigSource, /hasAvailableEmbeddingService/);
assert.match(matchConfigSource, /hasAvailableLlmService/);
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/smart-fill-default-config.test.ts
```

Expected: FAIL because all three controls are still rendered.

- [ ] **Step 3: Remove the form entries**

- Delete the Embedding and LLM service `<el-form-item>` blocks from `MatchConfig.vue`.
- Delete the global and table-level “过滤空行” form items.
- Remove display-only service model/status computed values and their unused imports.
- Retain runtime selection loading, `allowLlm`, service availability computed values, advanced LLM control guards, `refreshAiServices`, and `getServiceStatus`.

- [ ] **Step 4: Run the UI regression test and verify GREEN**

Run the Step 2 command.

Expected: PASS.

### Task 2: Force empty-row filtering at request boundaries

**Files:**
- Modify: `web/src/views/smart-fill/components/MatchConfig.vue`
- Modify: `web/src/views/smart-fill/composables/useSmartFillPreviewRequest.ts`
- Modify: `web/src/views/smart-fill/composables/useSmartFillPreviewRequest.test.ts`
- Modify: `web/src/views/smart-fill/smartFillExecution.helpers.ts`
- Modify: `web/src/views/smart-fill/composables/useSmartFillExecution.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Modify: `web/tests/smart-fill-execution-helpers.test.ts`
- Modify: `web/tests/smart-fill-ai-equivalence.test.ts`

**Interfaces:**
- Consumes: potentially stale `MatchConfig.filterEmptySourceRows` and table-level values.
- Produces: preview and execution request payloads whose global config and every table carry `filterEmptySourceRows: true`.

- [ ] **Step 1: Write failing request-boundary tests**

Extend the preview composable test with an input whose global and table values are `false`, capture `onSendPreview`, and assert:

```ts
expect(payload.config.filterEmptySourceRows).toBe(true);
expect(payload.tables[0].filterEmptySourceRows).toBe(true);
```

Update the execution helper test to pass global and table values as `false` and assert:

```ts
assert.equal(request.config?.filterEmptySourceRows, true);
assert.equal(request.tables[0].filterEmptySourceRows, true);
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
pnpm exec vitest run src/views/smart-fill/composables/useSmartFillPreviewRequest.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/smart-fill-execution-helpers.test.ts
```

Expected: FAIL because request payloads still honor or inherit `false`.

- [ ] **Step 3: Normalize preview requests**

In `useSmartFillPreviewRequest.ts`:

- Remove `getEffectiveFilterEmptySourceRows` from the options contract and destructuring.
- Set each table's `filterEmptySourceRows` to `true`.
- Send `config: { ...matchConfig.value, filterEmptySourceRows: true }`.

Remove the obsolete option from `index.vue` and its tests.

- [ ] **Step 4: Normalize execution requests**

In `buildSmartFillExecuteRequest`:

- Remove the resolver callback parameter.
- Set each table's `filterEmptySourceRows` to `true`.
- Set request config to `{ ...matchConfig, highConfidenceThreshold, filterEmptySourceRows: true }`.

Remove the obsolete resolver option from `useSmartFillExecution.ts`, `index.vue`, and affected tests.

- [ ] **Step 5: Normalize the editable model**

When `MatchConfig.vue` merges incoming `modelValue`, override `filterEmptySourceRows` to `true` so future emitted configurations cannot preserve a stale `false`.

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run the Step 2 commands plus:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/smart-fill-ai-equivalence.test.ts
```

Expected: PASS.

### Task 3: Targeted verification

**Files:**
- Verify all modified files above.

**Interfaces:**
- Consumes: completed Tasks 1 and 2.
- Produces: evidence that the scoped UI change compiles and preserves existing smart-fill behavior.

- [ ] **Step 1: Format changed files**

Run Prettier only on files changed by this plan.

- [ ] **Step 2: Run static checks**

Run:

```powershell
pnpm exec vue-tsc --noEmit
pnpm exec eslint src/views/smart-fill/components/MatchConfig.vue src/views/smart-fill/components/BatchTableConfig.vue src/views/smart-fill/composables/useSmartFillPreviewRequest.ts src/views/smart-fill/composables/useSmartFillPreviewRequest.test.ts src/views/smart-fill/composables/useSmartFillExecution.ts src/views/smart-fill/smartFillExecution.helpers.ts
git diff --check
```

- [ ] **Step 3: Re-run all focused tests**

Run the Task 1 and Task 2 test commands and confirm zero failures.

- [ ] **Step 4: Service smoke check**

Verify `http://127.0.0.1:8849` returns HTTP 200. Do not claim browser visual verification unless the browser runtime is available and the rendered page was inspected.
