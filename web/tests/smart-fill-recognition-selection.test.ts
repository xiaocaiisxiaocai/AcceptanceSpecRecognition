import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
  "utf8"
);
const sharedTabsSource = readFileSync(
  resolve(process.cwd(), "web/src/views/shared/SmartStructureConfirmTabs.vue"),
  "utf8"
);
const batchConfigSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/smart-fill/components/BatchTableConfig.vue"
  ),
  "utf8"
);

test("智能填充识别卡应允许勾选或取消参与表格", () => {
  assert.doesNotMatch(source, /:import-selectable="false"/);
  assert.match(source, /:selected-table-indexes="selectedTableIndexes"/);
  assert.match(source, /@update:table-selected=/);
  assert.match(source, /handleRecognizedTableSelectionChange/);
});

test("智能填充识别结果应与数据导入一致使用逐表 Tab", () => {
  assert.match(source, /<SmartStructureConfirmTabs/);
  assert.match(source, /v-model:active-table-index="activeSmartStructureTab"/);
  assert.match(sharedTabsSource, /v-for="table in tabItems"/);
  assert.match(sharedTabsSource, /readyLabel/);
  assert.match(sharedTabsSource, /"待确认"/);
  assert.doesNotMatch(source, /class="smart-fill-confirm-group"/);
});

test("智能填充识别成功后应进入独立识别确认步骤", () => {
  assert.match(source, /SMART_FILL_STEP_RECOGNITION_REVIEW/);
  assert.match(
    source,
    /batchTableConfigs\.value = configs;[\s\S]*currentStep\.value = SMART_FILL_STEP_RECOGNITION_REVIEW/
  );
  assert.match(
    source,
    /v-show="[\s\S]*currentStep === SMART_FILL_STEP_RECOGNITION_REVIEW[\s\S]*class="step-panel smart-fill-recognition-review"/
  );
  assert.match(source, /结构识别结果/);
});

test("识别失败应停留在上传步骤并保留重试入口", () => {
  assert.match(
    source,
    /currentStep\.value = SMART_FILL_STEP_UPLOAD_SCOPE;[\s\S]*const result = await recognizeSmartStructure[\s\S]*if \(!result\) return;/
  );
  assert.match(
    source,
    /<SmartStructureSummaryBanner[\s\S]*v-if="smartRecognitionError"[\s\S]*@retry="runSmartStructureRecognition"/
  );
});

test("识别成功但暂时不能生成填充配置时仍应进入确认页手动处理", () => {
  assert.match(
    source,
    /if \(configs\.length === 0\) \{[\s\S]*识别结果需要补充列配置，请在确认页手动处理[\s\S]*\}[\s\S]*batchTableConfigs\.value = configs;[\s\S]*currentStep\.value = SMART_FILL_STEP_RECOGNITION_REVIEW/
  );
  assert.doesNotMatch(
    source,
    /if \(configs\.length === 0\) \{[\s\S]{0,160}return;/
  );
});

test("智能填充上传页不应再内联展示完整逐表确认结果", () => {
  const uploadStepEnd = source.indexOf("</SmartFillUploadStep>");
  const uploadStepSource = source.slice(
    source.indexOf("<SmartFillUploadStep"),
    uploadStepEnd
  );
  assert.doesNotMatch(uploadStepSource, /<SmartStructureConfirmTabs/);
  assert.match(uploadStepSource, /结构识别结果已保留/);
});

test("删除上传文件必须让旧识别和匹配结果失效并返回上传步骤", () => {
  assert.match(source, /@update:uploaded-file="handleUploadedFileChange"/);
  assert.match(
    source,
    /const handleUploadedFileChange[\s\S]*if \(file\) return;[\s\S]*batchTableConfigs\.value = \[\];[\s\S]*batchPreviewResults\.value = \[\];[\s\S]*resetSmartStructure\(\);[\s\S]*currentStep\.value = SMART_FILL_STEP_UPLOAD_SCOPE/
  );
});

test("识别确认步骤应明确展示剩余待确认 Sheet 数并阻止跳过", () => {
  assert.match(source, /pendingSelectedSmartRecognitionCount/);
  assert.match(
    source,
    /还有 \$\{pendingSelectedSmartRecognitionCount\.value\} 个已选 Sheet 待确认/
  );
  assert.match(
    source,
    /case SMART_FILL_STEP_RECOGNITION_REVIEW:[\s\S]*return canContinueSmartRecognition\.value/
  );
});

test("客户切换和重新识别必须清空旧识别配置", () => {
  assert.match(source, /watch\([\s\S]*matchScope\.value\.customerId/);
  assert.match(
    source,
    /batchTableConfigs\.value = \[\];[\s\S]*resetSmartStructure\(\)/
  );
  assert.match(
    source,
    /const runSmartStructureRecognition[\s\S]*batchTableConfigs\.value = \[\];[\s\S]*recognizeSmartStructure/
  );
});

test("识别失败或 Reject 后应能从完整表元数据恢复手动配置", () => {
  assert.match(source, /ensureManualTableConfigs/);
  assert.match(
    source,
    /const enterAdvancedMode = \(\) => \{[\s\S]*ensureManualTableConfigs\(\)/
  );
});

test("智能填充高级编辑应保留多区域并在返回时同步识别摘要", () => {
  assert.doesNotMatch(batchConfigSource, /regions:\s*\[\]/);
  assert.match(batchConfigSource, /syncPrimaryBatchTableRegion/);
  assert.match(source, /syncSmartFillConfigsToRecognizedTables/);
  assert.match(
    source,
    /currentStep\.value === SMART_FILL_ADVANCED_STEP_TABLE_CONFIG[\s\S]*replaceRecognizedTables\([\s\S]*syncSmartFillConfigsToRecognizedTables/
  );
});

test("确认后的新可用表应自动参与智能填充", () => {
  assert.match(
    source,
    /selectedState\.get\(config\.tableIndex\) \?\?[\s\S]*config\.tableIndex === table\.tableIndex \? true/
  );
});
