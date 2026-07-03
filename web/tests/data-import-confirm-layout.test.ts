import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

const confirmPanelSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  ),
  "utf8"
);

const confirmCardSource = readFileSync(
  resolve(process.cwd(), "web/src/views/shared/SmartStructureConfirmCard.vue"),
  "utf8"
);
const dataImportSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);

test("数据导入确认页应把导入设置和待导入清单折叠，避免预览页被明细长表撑散", () => {
  assert.match(confirmPanelSource, /<el-collapse[\s\S]*confirm-panel-collapse/);
  assert.match(confirmPanelSource, /name="duplicate-ai"/);
  assert.match(confirmPanelSource, /name="preview-list"/);
  assert.match(confirmPanelSource, /导入设置/);
  assert.match(confirmPanelSource, /待导入清单/);
});

test("待导入清单不应使用内部纵向滚动，避免鼠标滚轮被表格区域截获", () => {
  assert.doesNotMatch(confirmPanelSource, /max-height="280"/);
});

test("智能结构确认卡片应支持默认折叠摘要态，减少多表确认时的纵向噪音", () => {
  assert.match(confirmCardSource, /const detailVisible = ref/);
  assert.match(confirmCardSource, /defaultExpanded\?: boolean/);
  assert.match(confirmCardSource, /class="card-summary-strip"/);
  assert.match(confirmCardSource, /展开配置|收起配置/);
  assert.match(confirmCardSource, /v-show="detailVisible"/);
});

test("数据导入确认页默认只展开第一张待确认表，避免多张表同时展开撑满首屏", () => {
  assert.match(dataImportSource, /firstNeedConfirmTableIndex/);
  assert.match(
    dataImportSource,
    /:default-expanded="[\s\S]*table\.tableIndex === firstNeedConfirmTableIndex[\s\S]*"/
  );
});

test("智能确认卡片应提供 Sheet 是否参与导入的勾选入口", () => {
  assert.match(confirmCardSource, /importSelected\?: boolean/);
  assert.match(confirmCardSource, /<el-checkbox/);
  assert.match(dataImportSource, /selectedSmartTableIndexes/);
  assert.match(dataImportSource, /handleSmartTableImportSelectionChange/);
});
