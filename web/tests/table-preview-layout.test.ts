import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const tablePreviewSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/TablePreview.vue"
  ),
  "utf8"
);

test("预览表应禁用自动压缩列宽，避免宽表被挤到不可读", () => {
  assert.match(tablePreviewSource, /fitColumns\?: boolean/);
  assert.match(
    tablePreviewSource,
    /withDefaults\([\s\S]*fitColumns:\s*false[\s\S]*<el-table[\s\S]*\s:fit="fitColumns"/
  );
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

test("少量映射列应支持为指定列设置紧凑宽度", () => {
  assert.match(
    tablePreviewSource,
    /mappedColumnWidths\?: Array<number \| undefined>/
  );
  assert.match(
    tablePreviewSource,
    /<el-table-column[\s\S]*:width="mappedColumnWidths\?\.\[colIndex\]"/
  );
});

test("预览表可显示源文件实际行号", () => {
  assert.match(tablePreviewSource, /rowNumberStart\?: number/);
  assert.match(
    tablePreviewSource,
    /v-if="rowNumberStart !== undefined"[\s\S]*label="行号"[\s\S]*rowNumberStart \+ \(tableData\.rowOffset \?\? 0\) \+ \$index/
  );
});

test("工作表预览请求不应再拉取整张表全部数据", () => {
  assert.doesNotMatch(tablePreviewSource, /previewRows:\s*0/);
});

test("表格预览应按请求参数缓存结果，避免切换页签重复请求相同预览", () => {
  assert.match(
    tablePreviewSource,
    /previewDataCache = new Map<string, TableData>/
  );
  assert.match(tablePreviewSource, /buildPreviewCacheKey/);
  assert.match(tablePreviewSource, /loadPreview\(forceRefresh = false\)/);
});
