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
