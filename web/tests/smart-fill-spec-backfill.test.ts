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
  assert.match(
    matchingApiSource,
    /decision\?: "overwrite" \| "create" \| "skip"/
  );
  assert.match(matchingApiSource, /skippedCount: number/);
  assert.match(matchingApiSource, /spec-backfill/);
  assert.match(matchingApiSource, /backfillSmartFillSpecs/);
});

test("smart-fill 执行填充前应弹出统一写库决策框", () => {
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

  assert.doesNotMatch(matchPreviewTableSource, /getEditedBackfillItems/);
  assert.match(batchPreviewTabsSource, /getAllBackfillCandidates/);
  assert.match(batchPreviewTabsSource, /collectSmartFillBackfillCandidates/);
  assert.match(backfillDialogSource, /确认验收规格写库方式/);
  assert.match(backfillDialogSource, /align-center/);
  assert.match(backfillDialogSource, /全部跳过并执行填充/);
  assert.match(backfillDialogSource, /按当前选择继续填充/);
  assert.match(backfillDialogSource, /全部覆盖/);
  assert.match(backfillDialogSource, /全部增加/);
  assert.match(backfillDialogSource, /全部跳过/);
  assert.match(backfillDialogSource, /覆盖已有/);
  assert.match(backfillDialogSource, /增加一条/);
  assert.match(backfillDialogSource, /跳过写库/);
  assert.match(backfillDialogSource, /getBackfillCandidateRowKey/);
  assert.match(
    backfillDialogSource,
    /`\$\{row\.tableIndex\}:\$\{row\.rowIndex\}`/
  );
  assert.match(backfillDialogSource, /label="Sheet \/ 行"/);
  assert.match(backfillDialogSource, /label="项目 \/ 规格（原 → 当前）"/);
  assert.match(backfillDialogSource, /label="验收标准（原 → 当前）"/);
  assert.match(backfillDialogSource, /label="备注（原 → 当前）"/);
  assert.match(
    backfillDialogSource,
    /row\.overrideAcceptance \?\? row\.originalAcceptance/
  );
  assert.match(
    backfillDialogSource,
    /row\.overrideRemark \?\? row\.originalRemark/
  );
  assert.match(backfillDialogSource, /backfill-change__old/);
  assert.match(backfillDialogSource, /backfill-change__new/);
  assert.match(executionSource, /backfillSmartFillSpecs/);
});

test("smart-fill 回填前应校验新增规格范围并透出真实错误", () => {
  const executionSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillExecution.ts"
  );

  assert.match(
    executionSource,
    /writeCandidates\.some\(item => item\.decision === "create"\)/
  );
  assert.match(executionSource, /回填新增规格前，请先选择客户范围/);
  assert.match(executionSource, /duplicateOverwriteSpecId/);
  assert.match(
    executionSource,
    /同一已有规格不能在一次操作中被多条记录同时覆盖/
  );
  assert.match(
    executionSource,
    /ElMessage\.error\(getRequestErrorMessage\(error, "回填或填充失败"\)\)/
  );
});

test("smart-fill 跳过资料库写入后仍应执行当前文件填充", () => {
  const executionSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillExecution.ts"
  );

  assert.match(
    executionSource,
    /const writeCandidates = candidates\.filter\(item => item\.decision !== "skip"\)/
  );
  assert.match(executionSource, /await runExecuteFill\(executeRequest\)/);
  assert.match(executionSource, /decision: item\.decision/);
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
