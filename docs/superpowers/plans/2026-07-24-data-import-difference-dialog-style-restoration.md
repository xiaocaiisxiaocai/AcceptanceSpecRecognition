# Data Import Difference Dialog Style Restoration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task.

**Goal:** Restore the intended structured, readable layout of the import-difference confirmation dialog while preserving all existing import decisions and data behavior.

**Architecture:** Keep the dialog shell responsible for viewport sizing and header/body/footer layout. Move the difference-content styles from the parent page's scoped stylesheet into a stylesheet owned by `DataImportDifferenceConfirmDialog.vue`, so Vue applies the same scope attribute to the markup and selectors. Keep the existing component structure and events unchanged.

**Tech Stack:** Vue 3 SFC, TypeScript, Element Plus, CSS, Node test runner, `@vue/compiler-sfc`

## Global Constraints

- Do not change duplicate-detection, decision, pagination, import, API, or database behavior.
- Preserve all existing local changes and do not commit or push Git.
- Use a restrained enterprise workbench style consistent with the surrounding application.
- Run only targeted frontend validation appropriate to this isolated UI fix.

---

### Task 1: Add a regression test for scoped style ownership

**Files:**
- Create: `web/tests/data-import-difference-dialog-style.test.ts`
- Test: `web/tests/data-import-difference-dialog-style.test.ts`

**Step 1: Write the failing test**

- Parse `DataImportDifferenceConfirmDialog.vue` with `@vue/compiler-sfc`.
- Assert the component owns a scoped external stylesheet.
- Compile that stylesheet with a deterministic scope id and assert the dialog summary/card/sheet selectors receive the child component's scope.
- Assert the dialog shell owns body sizing rules.

**Step 2: Run the test and verify RED**

Run:

```powershell
node --experimental-strip-types --import ./tests/setup-node-test-cwd.mjs --test ./tests/data-import-difference-dialog-style.test.ts
```

Expected: FAIL because the child component has no stylesheet and the shell has no layout rules.

### Task 2: Restore and refine the dialog layout

**Files:**
- Create: `web/src/views/data-import/components/DataImportDifferenceConfirmDialog.styles.css`
- Modify: `web/src/views/data-import/components/DataImportDifferenceConfirmDialog.vue`
- Modify: `web/src/views/data-import/components/DataImportDifferenceDialog.vue`
- Modify: `web/src/views/data-import/index.styles.css`

**Step 1: Move component-owned styles**

- Remove `.difference-dialog__*`, `.difference-card__*`, and `.difference-sheet__*` rules from the parent page stylesheet.
- Add them to the child component stylesheet.
- Attach the stylesheet with a scoped `<style src>` block.

**Step 2: Refine the visual hierarchy**

- Keep the warning summary and bulk actions fixed above the scroll region.
- Present each difference as a bordered card with metadata, two balanced comparison panels, highlighted changed cells, and a clear decision footer.
- Keep pagination below the scroll region.
- At widths up to 900px, stack comparison panels and simplify the field grid.

**Step 3: Constrain the dialog shell**

- Make the Element Plus dialog a flex column capped to the viewport.
- Give its body a flex layout with hidden outer overflow.
- Separate header and footer with subtle borders and stable padding.

**Step 4: Run the regression test and verify GREEN**

Run the Task 1 command again.

Expected: PASS.

### Task 3: Targeted verification

**Files:**
- Verify all files above.

**Step 1: Format and static validation**

Run:

```powershell
pnpm exec prettier --write src/views/data-import/components/DataImportDifferenceDialog.vue src/views/data-import/components/DataImportDifferenceConfirmDialog.vue src/views/data-import/components/DataImportDifferenceConfirmDialog.styles.css src/views/data-import/index.styles.css tests/data-import-difference-dialog-style.test.ts
pnpm exec vue-tsc --noEmit
pnpm exec eslint src/views/data-import/components/DataImportDifferenceDialog.vue src/views/data-import/components/DataImportDifferenceConfirmDialog.vue tests/data-import-difference-dialog-style.test.ts
git diff --check
```

**Step 2: Re-run the targeted test**

Run the Task 1 command and confirm it remains green after formatting.

**Step 3: Service smoke check**

- Verify the frontend on port `8849` still returns HTTP 200.
- If the browser automation runtime is compatible, inspect the rendered dialog at desktop and narrow widths; otherwise report that visual automation was unavailable without claiming it passed.
