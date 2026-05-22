import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const dataImportSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);

test("导入映射步骤的工作表页签应启用懒渲染，避免一次性挂载所有重组件", () => {
  assert.match(
    dataImportSource,
    /<el-tab-pane[\s\S]*\slazy[\s\S]*<TablePreview[\s\S]*<div class="mapping-section">/
  );
});

test("导入映射步骤的工作表页签应显示 Sheet 名，降低多 Sheet 切换识别成本", () => {
  assert.match(
    dataImportSource,
    /getTableConfigTabLabel\(cfg\)/
  );
  assert.match(
    dataImportSource,
    /cfg\.tableInfo\?\.name\?\.trim\(\)/
  );
});
