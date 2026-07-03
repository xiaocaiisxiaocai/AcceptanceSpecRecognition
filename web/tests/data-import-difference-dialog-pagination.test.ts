import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);
const pageComposableSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/composables/useDataImportPage.ts"
  ),
  "utf8"
);
const dialogSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportDifferenceConfirmDialog.vue"
  ),
  "utf8"
);

test("重复确认弹窗应维护分页状态，避免一次性渲染全部差异卡片", () => {
  assert.match(pageComposableSource, /const pendingDifferencePage = ref\(1\);/);
  assert.match(pageComposableSource, /const pendingDifferencePageSize = ref\(\d+\);/);
  assert.match(pageComposableSource, /const pagedPendingDifferences = computed<ImportPendingDifferenceWithTable\[]>\(\s*\(\) =>/);
});

test("重复确认弹窗列表应只渲染当前页数据", () => {
  assert.doesNotMatch(dialogSource, /v-for="item in pendingDifferences"/);
  assert.match(dialogSource, /v-for="item in pagedPendingDifferences"/);
});

test("重复确认弹窗应提供分页器以访问全部差异项", () => {
  assert.match(dialogSource, /<el-pagination/);
  assert.match(source, /v-model:pending-difference-page="pendingDifferencePage"/);
  assert.match(source, /v-model:pending-difference-page-size="pendingDifferencePageSize"/);
});
