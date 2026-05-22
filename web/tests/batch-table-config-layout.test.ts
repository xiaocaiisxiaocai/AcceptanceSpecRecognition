import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const batchTableConfigSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/components/BatchTableConfig.vue"),
  "utf8"
);

test("数据预览外层容器应使用 border-box，避免 width:100% 加内边距后把页面撑宽", () => {
  assert.match(batchTableConfigSource, /\.table-preview-wrap\s*\{[\s\S]*box-sizing:\s*border-box;/);
});

test("智能填充 Excel 行设置数字输入应使用导入数据同款左右按钮", () => {
  assert.doesNotMatch(batchTableConfigSource, /controls-position="right"/);
});

test("智能填充多 Sheet 配置页签应启用懒渲染，避免一次性加载全部预览", () => {
  assert.match(
    batchTableConfigSource,
    /<el-tab-pane[\s\S]*\slazy[\s\S]*<TablePreview/
  );
});

test("智能填充 Excel 字段下拉应显示本地列序号和 Excel 列字母", () => {
  assert.match(batchTableConfigSource, /toExcelColumnLetter/);
  assert.match(batchTableConfigSource, /第 \$\{i \+ 1\} 列/);
});
