import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const smartFillSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
  "utf8"
);
const smartFillPreviewStepSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/smart-fill/components/SmartFillPreviewStep.vue"
  ),
  "utf8"
);

const batchPreviewTabsSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/components/BatchPreviewTabs.vue"),
  "utf8"
);

test("智能填充匹配结果页应把 Sheet 名传给预览 Tab", () => {
  assert.match(smartFillSource, /previewTableNames/);
  assert.match(smartFillSource, /:preview-table-names="previewTableNames"/);
  assert.match(smartFillPreviewStepSource, /:table-names="previewTableNames"/);
});

test("智能填充匹配结果 Tab 标签应优先显示 Sheet 名", () => {
  assert.match(batchPreviewTabsSource, /tableNames\?: Record<number, string>/);
  assert.match(batchPreviewTabsSource, /getTableTabLabel\(tableResult\)/);
});
