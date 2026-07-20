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
