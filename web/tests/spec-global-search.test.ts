import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const specTableSource = readFileSync(
  resolve(process.cwd(), "web/src/views/base-data/specs/components/SpecTable.vue"),
  "utf8"
);

test("验收规格应提供全局搜索入口", () => {
  assert.match(specTableSource, /globalSearch/);
  assert.match(specTableSource, /全局搜索/);
  assert.match(specTableSource, /项目\/规格\/验收标准\/备注/);
});

test("验收规格全局搜索请求不应携带当前分组条件", () => {
  assert.match(specTableSource, /if\s*\(\s*queryParams\.globalSearch\s*\)\s*\{/);
  assert.match(specTableSource, /return params;/);
  assert.match(specTableSource, /params\.customerId = props\.customerId/);
  assert.match(specTableSource, /params\.machineModelId = props\.machineModelId/);
  assert.match(specTableSource, /params\.processId = props\.processId/);
});

test("验收规格全局搜索结果应显示来源范围", () => {
  assert.match(specTableSource, /v-if="queryParams\.globalSearch"/);
  assert.match(specTableSource, /prop="customerName"/);
  assert.match(specTableSource, /prop="machineModelName"/);
  assert.match(specTableSource, /prop="processName"/);
});
