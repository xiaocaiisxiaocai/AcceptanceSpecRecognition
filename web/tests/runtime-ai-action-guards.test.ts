import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const readSource = (path: string) => readFileSync(path, "utf8");
const dataImportTargetSource = readSource(
  "./web/src/views/data-import/composables/useDataImportTarget.ts"
);
const dataImportPageSource = readSource(
  "./web/src/views/data-import/composables/useDataImportPage.ts"
);
const dataImportExecutionSource = readSource(
  "./web/src/views/data-import/composables/useDataImportBatchExecution.ts"
);
const matchConfigSource = readSource(
  "./web/src/views/smart-fill/components/MatchConfig.vue"
);
const smartFillPageSource = readSource("./web/src/views/smart-fill/index.vue");
const smartFillExecutionSource = readSource(
  "./web/src/views/smart-fill/composables/useSmartFillExecution.ts"
);

const functionSection = (source: string, start: string, end: string) => {
  const startIndex = source.indexOf(start);
  const endIndex = source.indexOf(end, startIndex + start.length);
  assert.notEqual(startIndex, -1, `missing section start: ${start}`);
  assert.notEqual(endIndex, -1, `missing section end: ${end}`);
  return source.slice(startIndex, endIndex);
};

test("AI purpose status requests are settled independently without loader toasts", () => {
  for (const source of [dataImportTargetSource, matchConfigSource]) {
    const loader = functionSection(
      source,
      "const loadAiServicesOnce",
      "const aiSelectionRetry"
    );
    assert.match(loader, /loadRuntimeAiSelectionsSettled/);
    assert.doesNotMatch(loader, /Promise\.all\(/);
    assert.doesNotMatch(loader, /ElMessage\.(error|warning)/);
    assert.match(loader, /current: false/);
    assert.match(loader, /current: true/);
  }
});

test("data-import initial and difference-continuation actions share the runtime guard", () => {
  assert.match(
    dataImportPageSource,
    /ensureRuntimeAiReady:\s*ensureImportRuntimeAiReady/
  );
  assert.match(
    functionSection(
      dataImportPageSource,
      "const handleImport = async",
      "const pendingDifferenceDisplayStart"
    ),
    /await ensureImportRuntimeAiReady\(\)/
  );

  const continuation = functionSection(
    dataImportExecutionSource,
    "const handleConfirmPendingDifferences = async",
    "const handleImport = async"
  );
  const guardIndex = continuation.indexOf(
    "await options.ensureRuntimeAiReady()"
  );
  assert.ok(guardIndex >= 0);
  assert.ok(
    guardIndex < continuation.indexOf("options.importing.value = true")
  );
  assert.ok(guardIndex < continuation.indexOf("executeImportBatch("));
});

test("every smart-fill execution path guards before side effects", () => {
  assert.match(
    smartFillPageSource,
    /ensureRuntimeAiReady:\s*refreshRuntimeAiSelection/
  );
  assert.match(smartFillPageSource, /if \(!refresh\?\.current\) return false;/);

  const withoutBackfill = functionSection(
    smartFillExecutionSource,
    "const executePendingWithoutBackfill = async",
    "const confirmBackfillAndExecute = async"
  );
  assert.ok(
    withoutBackfill.indexOf("await ensureRuntimeAiReady()") <
      withoutBackfill.indexOf("closeBackfillDialog()")
  );

  const withBackfill = functionSection(
    smartFillExecutionSource,
    "const confirmBackfillAndExecute = async",
    "const handleExecute = async"
  );
  assert.ok(
    withBackfill.indexOf("await ensureRuntimeAiReady()") <
      withBackfill.indexOf("setBackfillingSpecs(true)")
  );
  assert.ok(
    withBackfill.indexOf("await ensureRuntimeAiReady()") <
      withBackfill.indexOf("backfillSmartFillSpecs(")
  );

  const directExecute = functionSection(
    smartFillExecutionSource,
    "const handleExecute = async",
    "const resetExecutionState"
  );
  assert.ok(
    directExecute.indexOf("await ensureRuntimeAiReady()") <
      directExecute.indexOf("buildExecuteFillRequest(")
  );
  assert.ok(
    directExecute.indexOf("await ensureRuntimeAiReady()") <
      directExecute.indexOf("runExecuteFill(")
  );
});
