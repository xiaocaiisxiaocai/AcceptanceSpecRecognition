import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readSource = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

const apiSource = readSource("web/src/api/spec.ts");
const tableSource = readSource(
  "web/src/views/base-data/specs/components/SpecTable.vue"
);
const dialogSource = readSource(
  "web/src/views/base-data/specs/components/SpecRemarkReplaceDialog.vue"
);

test("备注批量替换应只在明确部门内开放", () => {
  assert.match(tableSource, /btn:spec:remark-replace/);
  assert.match(tableSource, /effectiveOperationOrgUnitId/);
  assert.match(tableSource, /请先在上方数据范围选择具体部门/);
  assert.match(tableSource, /:org-unit-id="effectiveOperationOrgUnitId"/);
});

test("备注批量替换应先预览再确认执行", () => {
  assert.match(apiSource, /previewSpecRemarkReplace/);
  assert.match(apiSource, /executeSpecRemarkReplace/);
  assert.match(dialogSource, /affectedSpecCount/);
  assert.match(dialogSource, /matchCount/);
  assert.match(dialogSource, /confirmationToken/);
  assert.match(dialogSource, /确认替换/);
  assert.match(dialogSource, /重新预览/);
});

test("替换成功后应通知规格表刷新相关搜索结果", () => {
  assert.match(tableSource, /handleRemarkReplaceSuccess/);
  assert.match(tableSource, /reloadSemanticSearchIfNeeded/);
  assert.match(tableSource, /duplicateResult\.value = null/);
  assert.match(tableSource, /emit\("data-change"\)/);
});
