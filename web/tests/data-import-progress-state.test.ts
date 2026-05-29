import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);
const batchExecutionSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/composables/useDataImportBatchExecution.ts"),
  "utf8"
);

test("导入页应维护显式进度文案，避免长时间 AI 去重时只有处理中提示", () => {
  assert.match(source, /const importProgressText = ref\(""\);/);
  assert.match(source, /importProgressDescription,/);
  assert.match(source, /importPrimaryButtonText,/);
  assert.match(source, /confirmDifferenceButtonText/);
  assert.match(source, /const differenceDialogFooterTip = computed\(\(\) =>/);
});

test("导入页在执行中应展示进度提示面板", () => {
  assert.match(source, /v-if="importing" class="import-progress-panel"/);
  assert.match(source, /\{\{ importProgressText \}\}/);
  assert.match(source, /\{\{ importProgressDescription \}\}/);
});

test("导入按钮与重复确认按钮应复用更明确的进度文案", () => {
  assert.match(source, /\{\{\s*importPrimaryButtonText\s*\}\}/);
  assert.match(source, /\{\{\s*confirmDifferenceButtonText\s*\}\}/);
  assert.match(source, /\{\{\s*differenceDialogFooterTip\s*\}\}/);
  assert.match(source, /: `未确认 \$\{pendingUndecidedCount\.value\} 条`;/);
  assert.match(
    batchExecutionSource,
    /ElMessage\.warning\(`请先逐条确认重复项（仍有 \$\{options\.pendingUndecidedCount\.value\} 条未确认）`\);/
  );
  assert.match(source, /<span>未确认 \{\{ pendingUndecidedCount \}\} 条<\/span>/);
});

test("导入页执行导入或确认重复项失败时应显式提示真实错误", () => {
  assert.match(
    batchExecutionSource,
    /ElMessage\.error\(error instanceof Error \? error\.message : "导入失败，请稍后重试"\);/
  );
  assert.match(
    batchExecutionSource,
    /ElMessage\.error\(error instanceof Error \? error\.message : "继续导入失败，请稍后重试"\);/
  );
});

test("导入页 AI 疑似重复默认配置应复用共享 helper", () => {
  assert.match(source, /createDefaultImportDuplicateAiConfig/);
  assert.match(
    source,
    /ref<ImportDuplicateAiConfig>\(\s*createDefaultImportDuplicateAiConfig\(\)\s*\)/
  );
  assert.match(
    source,
    /createDefaultImportDuplicateAiConfig\(\{\s*embeddingServiceId: embeddingServices\.value\[0\]\?\.id,\s*llmServiceId: llmServices\.value\[0\]\?\.id\s*\}\)/
  );
  assert.doesNotMatch(source, /importDuplicateAiConfig\.value = \{\s*enableSemanticDuplicateCheck: false/);
});

test("导入页上传新文件与重新开始应复用统一的流程重置 helper", () => {
  assert.match(source, /const resetImportFlowState = \(/);
  assert.match(
    source,
    /const handleFileUploaded = \(file: FileUploadResponse\) => \{\s*resetImportFlowState\(\{ preserveTargetSelection: true \}\);/s
  );
  assert.match(
    source,
    /const handleRestart = \(\) => \{\s*resetImportFlowState\(\);/s
  );
});

test("导入页应复用独立的聚合 helper，避免主文件继续堆积纯结果拼装逻辑", () => {
  assert.match(batchExecutionSource, /from "\.\.\/dataImport\.execution\.helpers";/);
  assert.doesNotMatch(source, /const buildEmptyImportAggregate = \(\): CombinedImportResult =>/);
  assert.doesNotMatch(source, /const mergeImportAggregates = \(/);
  assert.doesNotMatch(source, /const createSingleTableAggregate = \(/);
  assert.doesNotMatch(source, /const splitBatchAggregates = \(/);
});

test("导入页应把批量执行链路下沉到 composable，页面只保留交互编排", () => {
  assert.match(source, /useDataImportBatchExecution/);
  assert.match(batchExecutionSource, /const executeImportBatch = async \(/);
  assert.match(batchExecutionSource, /const buildDuplicateCheckOptions = \(\)/);
  assert.match(batchExecutionSource, /const validateDuplicateAiConfig = \(\)/);
  assert.match(batchExecutionSource, /const buildImportProgressText = \(/);
  assert.match(batchExecutionSource, /const clearImportProgress = \(\)/);
  assert.doesNotMatch(source, /const executeImportBatch = async \(/);
  assert.doesNotMatch(source, /const buildDuplicateCheckOptions = \(\)/);
  assert.doesNotMatch(source, /const validateDuplicateAiConfig = \(\)/);
  assert.doesNotMatch(source, /const buildImportProgressText = \(/);
});

test("导入页应复用差异展示格式化 helper，避免弹窗展示逻辑继续堆在页面里", () => {
  assert.match(source, /from "\.\/dataImport\.difference-formatters";/);
  assert.doesNotMatch(source, /const formatDifferenceValue = \(/);
  assert.doesNotMatch(source, /const formatScorePercent = \(/);
  assert.doesNotMatch(source, /const getDifferenceMatchTypeLabel = \(/);
  assert.doesNotMatch(source, /const getDifferenceMatchTypeTagType = \(/);
  assert.doesNotMatch(source, /const hasAiDifferenceMeta = \(/);
  assert.doesNotMatch(source, /const isDifferenceFieldChanged = \(/);
  assert.doesNotMatch(source, /const differenceColumnDefs: DifferenceColumnDef\[\] = /);
  assert.doesNotMatch(source, /const isDifferenceColumnChanged = \(/);
});
