import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

test("smart-fill 应提供编辑值回填验收规格 API 封装", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.match(matchingApiSource, /SmartFillSpecBackfillRequest/);
  assert.match(matchingApiSource, /SmartFillSpecBackfillResponse/);
  assert.match(matchingApiSource, /spec-backfill/);
  assert.match(matchingApiSource, /backfillSmartFillSpecs/);
});

test("smart-fill 执行填充前应弹出编辑值回填确认框", () => {
  const backfillDialogSource = readProjectFile(
    "web/src/views/smart-fill/components/SmartFillBackfillDialog.vue"
  );
  const executionSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillExecution.ts"
  );
  const batchPreviewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const matchPreviewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(matchPreviewTableSource, /getEditedBackfillItems/);
  assert.match(batchPreviewTabsSource, /getAllEditedBackfillItems/);
  assert.match(backfillDialogSource, /回填验收规格/);
  assert.match(backfillDialogSource, /不回填，仅执行填充/);
  assert.match(backfillDialogSource, /确认回填并执行填充/);
  assert.match(backfillDialogSource, /getBackfillCandidateRowKey/);
  assert.match(
    backfillDialogSource,
    /`\$\{row\.tableIndex\}:\$\{row\.rowIndex\}`/
  );
  assert.match(backfillDialogSource, /label="表格\/行"/);
  assert.match(executionSource, /backfillSmartFillSpecs/);
});

test("smart-fill 回填前应校验新增规格范围并透出真实错误", () => {
  const executionSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillExecution.ts"
  );

  assert.match(
    executionSource,
    /selected\.some\(item => item\.actionType === "create"\)/
  );
  assert.match(executionSource, /回填新增规格前，请先选择客户范围/);
  assert.match(
    executionSource,
    /ElMessage\.error\(getRequestErrorMessage\(error, "回填或填充失败"\)\)/
  );
});

test("smart-fill 应缓存匹配范围并在执行回填时复用", () => {
  const smartFillSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(smartFillSource, /const matchScope = ref<SmartFillScope>\(/);
  assert.match(smartFillSource, /const handleScopeChange = \(/);
  assert.match(
    smartFillSource,
    /const getCurrentScope = \(\) => matchScope\.value/
  );
  assert.doesNotMatch(smartFillSource, /@scope-change="handleScopeChange"/);
});

test("smart-fill 重新开始或重新上传文件时应清空回填待执行状态和范围缓存", () => {
  const smartFillSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const backfillStateSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillBackfillState.ts"
  );

  assert.match(
    backfillStateSource,
    /const resetPendingBackfillState = \(\) => \{/
  );
  assert.match(backfillStateSource, /pendingExecuteRequest\.value = null;/);
  assert.match(backfillStateSource, /backfillCandidates\.value = \[\];/);
  assert.match(backfillStateSource, /backfillDialogVisible\.value = false;/);
  assert.match(smartFillSource, /matchScope\.value = \{/);
  assert.match(
    smartFillSource,
    /const handleFileUploaded = async \(file: FileUploadResponse\) => \{[\s\S]*resetPendingBackfillState\(\);/
  );
  assert.match(
    smartFillSource,
    /const handleRestart = \(\) => \{[\s\S]*resetPendingBackfillState\(\);/
  );
});
