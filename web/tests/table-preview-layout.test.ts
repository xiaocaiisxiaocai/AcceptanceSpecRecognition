import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const tablePreviewSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/components/TablePreview.vue"),
  "utf8"
);

test("预览表应禁用自动压缩列宽，避免宽表被挤到不可读", () => {
  assert.match(tablePreviewSource, /<el-table[\s\S]*\s:fit="false"/);
});

test("预览表容器应显式开启横向滚动", () => {
  assert.match(tablePreviewSource, /\.table-container\s*\{[\s\S]*overflow-x:\s*auto;/);
});

test("预览表列头不应支持手动拖拽改列宽", () => {
  assert.match(tablePreviewSource, /<el-table-column[\s\S]*:resizable="false"/);
});
