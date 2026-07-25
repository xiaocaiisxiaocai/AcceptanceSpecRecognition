# Runtime AI Dual-Service Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Require both runtime LLM and Embedding services to be connected before AI-assisted recognition can be enabled, and show both selected service models in data import and Smart Fill.

**Architecture:** Extend the existing pure AI-assist state resolver to accept separate LLM and Embedding selections, keeping enablement logic independently testable. Update the shared `SmartStructureAiAssistControl.vue` to load both purposes with the existing settled loader, retry both together, render two compact service rows, and continue emitting only the LLM service ID consumed by structure recognition.

**Tech Stack:** Vue 3 Composition API, TypeScript, Element Plus, Vitest, Node test runner

## Global Constraints

- LLM and Embedding must both be `available` with service IDs before the switch can be enabled.
- Data import and Smart Fill must share one implementation and identical Chinese labels.
- Structure-recognition request contracts remain unchanged and continue receiving only the LLM service ID.
- A failed or stale request for one purpose must not overwrite the other purpose's current result.
- No backend API, database, or AI selection algorithm changes.
- Run only focused frontend tests and checks appropriate for this scoped UI change.

---

### Task 1: Dual-Service Enablement State

**Files:**
- Modify: `web/src/views/shared/ai-selection-state.ts`
- Test: `web/src/views/shared/ai-selection-state.test.ts`

**Interfaces:**
- Consumes: `AiServiceSelection` from `@/api/ai-service`
- Produces: `resolveAiAssistSelectionState(llmSelection: AiServiceSelection, embeddingSelection: AiServiceSelection): AiAssistSelectionState`

- [ ] **Step 1: Write failing dual-service state tests**

Replace single-selection calls with two selections and add cases proving either missing purpose disables the feature:

```ts
const available = (serviceId: number, name: string): AiServiceSelection => ({
  status: "available",
  serviceId,
  name
});

expect(
  resolveAiAssistSelectionState(available(7, "LLM"), available(8, "Embedding"))
).toEqual({ enabled: true, serviceId: 7 });

expect(
  resolveAiAssistSelectionState(
    available(7, "LLM"),
    { status: "unavailable" }
  )
).toEqual({ enabled: false, serviceId: undefined });
```

Cover LLM `checking`/`unavailable` and Embedding `checking`/`unavailable`.

- [ ] **Step 2: Run the state test and verify RED**

Run:

```powershell
Set-Location web
pnpm exec vitest run src/views/shared/ai-selection-state.test.ts
```

Expected: FAIL because `resolveAiAssistSelectionState` accepts only one selection and enables when only LLM is available.

- [ ] **Step 3: Implement the dual-service resolver**

Change the resolver to require both selections:

```ts
export const resolveAiAssistSelectionState = (
  llmSelection: AiServiceSelection,
  embeddingSelection: AiServiceSelection
): AiAssistSelectionState => {
  const llmReady =
    llmSelection.status === "available" && llmSelection.serviceId != null;
  const embeddingReady =
    embeddingSelection.status === "available" &&
    embeddingSelection.serviceId != null;

  if (!llmReady || !embeddingReady) {
    return { enabled: false, serviceId: undefined };
  }

  return { enabled: true, serviceId: llmSelection.serviceId ?? undefined };
};
```

- [ ] **Step 4: Run the state test and verify GREEN**

Run:

```powershell
Set-Location web
pnpm exec vitest run src/views/shared/ai-selection-state.test.ts
```

Expected: all state tests PASS.

- [ ] **Step 5: Commit the state resolver**

```powershell
git add web/src/views/shared/ai-selection-state.ts web/src/views/shared/ai-selection-state.test.ts
git commit -m "feat: require dual AI service readiness"
```

### Task 2: Shared Dual-Service Loading and Display

**Files:**
- Modify: `web/src/views/shared/SmartStructureAiAssistControl.vue`
- Test: `web/tests/smart-config-operational-guards.test.ts`

**Interfaces:**
- Consumes: `loadRuntimeAiSelectionsSettled(["embedding", "llm"], signal)` and `getRuntimeAiPurposeResult(results, purpose)`
- Consumes: `resolveAiAssistSelectionState(llmSelection, embeddingSelection)` from Task 1
- Produces: the existing `update:enabled` and `update:serviceId` events; only the ready LLM ID is emitted

- [ ] **Step 1: Write failing shared-component contract assertions**

Update the operational guard test to require:

```ts
assert.match(
  controlSource,
  /loadRuntimeAiSelectionsSettled\(\s*\["embedding",\s*"llm"\]/
);
assert.match(
  controlSource,
  /resolveAiAssistSelectionState\(\s*llmSelection\.value,\s*embeddingSelection\.value\s*\)/
);
assert.match(controlSource, />LLM</);
assert.match(controlSource, />Embedding</);
assert.match(controlSource, /llmServiceModel/);
assert.match(controlSource, /embeddingServiceModel/);
```

Keep the existing assertions that both data import and Smart Fill use the shared component and automatically synchronize enablement/service ID.

- [ ] **Step 2: Run the component contract test and verify RED**

Run:

```powershell
Set-Location web
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/smart-config-operational-guards.test.ts
```

Expected: FAIL because the component still requests and displays only LLM.

- [ ] **Step 3: Implement settled dual-purpose loading**

In `SmartStructureAiAssistControl.vue`:

- Replace the single `selection` ref with `llmSelection` and `embeddingSelection`.
- Replace the direct `getAiServiceSelection("llm")` call with
  `loadRuntimeAiSelectionsSettled(["embedding", "llm"], controller.signal)`.
- Resolve each purpose independently with `getRuntimeAiPurposeResult`.
- Feed both selections to `resolveAiAssistSelectionState`.
- Schedule retries with both selections.
- Treat cancellation results as stale and return without emitting.
- When either selection is not ready, emit `update:enabled(false)` and clear `update:serviceId`.

Use a computed readiness condition equivalent to:

```ts
const hasServices = computed(
  () =>
    llmSelection.value.status === "available" &&
    llmSelection.value.serviceId != null &&
    embeddingSelection.value.status === "available" &&
    embeddingSelection.value.serviceId != null
);
```

- [ ] **Step 4: Render both selected services and status messages**

When both services are ready and the feature is enabled, render two compact rows:

```vue
<div class="structure-ai-service-row">
  <span class="structure-ai-service-label">LLM</span>
  <span class="structure-ai-service-name">{{ llmSelection.name }}</span>
  <span v-if="llmServiceModel" class="structure-ai-service-model">
    {{ llmServiceModel }}
  </span>
</div>
<div class="structure-ai-service-row">
  <span class="structure-ai-service-label">Embedding</span>
  <span class="structure-ai-service-name">{{ embeddingSelection.name }}</span>
  <span v-if="embeddingServiceModel" class="structure-ai-service-model">
    {{ embeddingServiceModel }}
  </span>
</div>
```

Build the unavailable title from the individual LLM and Embedding statuses so the user can see which prerequisite is checking or unavailable. Preserve configuration and retry actions.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run:

```powershell
Set-Location web
pnpm exec vitest run src/views/shared/ai-selection-state.test.ts src/views/shared/ai-service-display.test.ts
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/smart-config-operational-guards.test.ts
pnpm typecheck
pnpm exec prettier --check src/views/shared/SmartStructureAiAssistControl.vue src/views/shared/ai-selection-state.ts src/views/shared/ai-selection-state.test.ts
Set-Location ..
git diff --check
```

Expected: all focused tests pass, typecheck exits `0`, Prettier reports all files formatted, and `git diff --check` has no output.

- [ ] **Step 6: Commit the shared component**

```powershell
git add web/src/views/shared/SmartStructureAiAssistControl.vue web/tests/smart-config-operational-guards.test.ts
git commit -m "feat: show dual AI service readiness"
```

### Task 3: Runtime Verification

**Files:**
- No source changes expected

**Interfaces:**
- Verifies: API `5291`, web `8849`, data-import and Smart Fill shared source bindings

- [ ] **Step 1: Confirm service health and source reuse**

Run:

```powershell
Invoke-WebRequest http://127.0.0.1:5291/health/live -UseBasicParsing
Invoke-WebRequest http://127.0.0.1:5291/health/ready -UseBasicParsing
Invoke-WebRequest http://127.0.0.1:8849/ -UseBasicParsing
rg -n "SmartStructureAiAssistControl" web/src/views/data-import/index.vue web/src/views/smart-fill/index.vue
```

Expected: all three HTTP checks return `200`; both pages reference the same shared component.

- [ ] **Step 2: Verify final Git scope**

Run:

```powershell
git status --short --branch
git log -4 --oneline
```

Expected: only the planned local commits are ahead of `origin/main`; no unrelated working-tree changes exist.
