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
  assert.match(
    tablePreviewSource,
    /\.table-container\s*\{[\s\S]*overflow:\s*auto hidden;/
  );
});

test("预览表列头不应支持手动拖拽改列宽", () => {
  assert.match(tablePreviewSource, /<el-table-column[\s\S]*:resizable="false"/);
});

test("工作表预览请求不应再拉取整张表全部数据", () => {
  assert.doesNotMatch(tablePreviewSource, /previewRows:\s*0/);
});

test("表格预览应按请求参数缓存结果，避免切换页签重复请求相同预览", () => {
  assert.match(tablePreviewSource, /previewDataCache = new Map<string, TableData>/);
  assert.match(tablePreviewSource, /buildPreviewCacheKey/);
  assert.match(tablePreviewSource, /loadPreview\(forceRefresh = false\)/);
});
