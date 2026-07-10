import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const pageComposableSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/composables/useDataImportPage.ts"
  ),
  "utf8"
);
const dataImportStoreSource = readFileSync(
  resolve(process.cwd(), "web/src/store/modules/dataImport.ts"),
  "utf8"
);
const confirmPanelSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  ),
  "utf8"
);
const differenceDialogSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportDifferenceConfirmDialog.vue"
  ),
  "utf8"
);
const batchExecutionSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/composables/useDataImportBatchExecution.ts"
  ),
  "utf8"
);

test("导入页应维护显式进度文案，避免长时间 AI 去重时只有处理中提示", () => {
  assert.match(pageComposableSource, /const importProgressText = ref\(""\);/);
  assert.match(pageComposableSource, /importProgressDescription,/);
  assert.match(pageComposableSource, /importPrimaryButtonText,/);
  assert.match(pageComposableSource, /confirmDifferenceButtonText/);
  assert.match(
    pageComposableSource,
    /const differenceDialogFooterTip = computed\(\(\) =>/
  );
});

test("导入页在执行中应在摘要操作栏展示进度提示", () => {
  assert.match(
    confirmPanelSource,
    /v-if="importing" class="import-summary-bar__progress"/
  );
  assert.match(confirmPanelSource, /\{\{ importProgressText \}\}/);
  assert.match(confirmPanelSource, /\{\{ importProgressDescription \}\}/);
});

test("导入按钮与重复确认按钮应复用更明确的进度文案", () => {
  assert.match(confirmPanelSource, /\{\{\s*importPrimaryButtonText\s*\}\}/);
  assert.match(
    differenceDialogSource,
    /\{\{\s*confirmDifferenceButtonText\s*\}\}/
  );
  assert.match(
    differenceDialogSource,
    /\{\{\s*differenceDialogFooterTip\s*\}\}/
  );
  assert.match(
    pageComposableSource,
    /: `未确认 \$\{pendingUndecidedCount\.value\} 条`;/
  );
  assert.match(
    batchExecutionSource,
    /ElMessage\.warning\(\s*`请先逐条确认重复项（仍有 \$\{options\.pendingUndecidedCount\.value\} 条未确认）`\s*\);/
  );
  assert.match(
    differenceDialogSource,
    /<span>未确认 \{\{ pendingUndecidedCount \}\} 条<\/span>/
  );
});

test("导入页执行导入或确认重复项失败时应显式提示真实错误", () => {
  assert.match(
    batchExecutionSource,
    /ElMessage\.error\(\s*error instanceof Error \? error\.message : "导入失败，请稍后重试"\s*\);/
  );
  assert.match(
    batchExecutionSource,
    /ElMessage\.error\(\s*error instanceof Error \? error\.message : "继续导入失败，请稍后重试"\s*\);/
  );
});

test("导入页 AI 疑似重复默认配置应复用共享 helper", () => {
  assert.match(
    `${pageComposableSource}\n${batchExecutionSource}`,
    /createDefaultImportDuplicateAiConfig/
  );
  assert.match(
    dataImportStoreSource,
    /const importDuplicateAiConfig = ref<ImportDuplicateAiConfig>\(\s*createDefaultImportDuplicateAiConfig\(\)\s*\)/
  );
  assert.match(
    pageComposableSource,
    /createDefaultImportDuplicateAiConfig\(\{\s*embeddingServiceId: embeddingServices\.value\[0\]\?\.id,\s*llmServiceId: llmServices\.value\[0\]\?\.id\s*\}\)/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /importDuplicateAiConfig\.value = \{\s*enableSemanticDuplicateCheck: false/
  );
});

test("导入页上传新文件与重新开始应复用统一的流程重置 helper", () => {
  assert.match(pageComposableSource, /const resetImportFlowState = \(/);
  assert.match(
    pageComposableSource,
    /const handleFileUploaded = \(file: FileUploadResponse\) => \{\s*resetImportFlowState\(\{ preserveTargetSelection: true \}\);/s
  );
  assert.match(
    pageComposableSource,
    /const handleRestart = \(\) => \{\s*resetImportFlowState\(\);/s
  );
});

test("导入页应复用独立的聚合 helper，避免主文件继续堆积纯结果拼装逻辑", () => {
  assert.match(
    batchExecutionSource,
    /from "\.\.\/dataImport\.execution\.helpers";/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const buildEmptyImportAggregate = \(\): CombinedImportResult =>/
  );
  assert.doesNotMatch(pageComposableSource, /const mergeImportAggregates = \(/);
  assert.doesNotMatch(
    pageComposableSource,
    /const createSingleTableAggregate = \(/
  );
  assert.doesNotMatch(pageComposableSource, /const splitBatchAggregates = \(/);
});

test("导入页应把批量执行链路下沉到 composable，页面只保留交互编排", () => {
  assert.match(pageComposableSource, /useDataImportBatchExecution/);
  assert.match(batchExecutionSource, /const executeImportBatch = async \(/);
  assert.match(batchExecutionSource, /const buildDuplicateCheckOptions = \(\)/);
  assert.match(batchExecutionSource, /const validateDuplicateAiConfig = \(\)/);
  assert.match(batchExecutionSource, /const buildImportProgressText = \(/);
  assert.match(batchExecutionSource, /const clearImportProgress = \(\)/);
  assert.doesNotMatch(
    pageComposableSource,
    /const executeImportBatch = async \(/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const buildDuplicateCheckOptions = \(\)/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const validateDuplicateAiConfig = \(\)/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const buildImportProgressText = \(/
  );
});

test("导入页应复用差异展示格式化 helper，避免弹窗展示逻辑继续堆在页面里", () => {
  assert.match(
    differenceDialogSource,
    /from "\.\.\/dataImport\.difference-formatters";/
  );
  assert.doesNotMatch(pageComposableSource, /const formatDifferenceValue = \(/);
  assert.doesNotMatch(pageComposableSource, /const formatScorePercent = \(/);
  assert.doesNotMatch(
    pageComposableSource,
    /const getDifferenceMatchTypeLabel = \(/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const getDifferenceMatchTypeTagType = \(/
  );
  assert.doesNotMatch(pageComposableSource, /const hasAiDifferenceMeta = \(/);
  assert.doesNotMatch(
    pageComposableSource,
    /const isDifferenceFieldChanged = \(/
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const differenceColumnDefs: DifferenceColumnDef\[\] = /
  );
  assert.doesNotMatch(
    pageComposableSource,
    /const isDifferenceColumnChanged = \(/
  );
});
