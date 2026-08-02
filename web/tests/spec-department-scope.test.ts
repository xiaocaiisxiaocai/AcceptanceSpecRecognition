import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readSource = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

const indexSource = readSource("web/src/views/base-data/specs/index.vue");
const tableSource = readSource(
  "web/src/views/base-data/specs/components/SpecTable.vue"
);
const semanticSource = readSource(
  "web/src/views/base-data/specs/components/SpecSemanticSearchDialog.vue"
);
const apiSource = readSource("web/src/api/spec.ts");

test("规格页应由业务组织上下文驱动总体和部门范围", () => {
  assert.match(indexSource, /getBusinessOrgContext/);
  assert.match(indexSource, /placeholder="公司总体"/);
  assert.match(indexSource, /getSpecGroups\([\s\S]*orgUnitId/);
  assert.match(indexSource, /:org-unit-id="queryOrgUnitId"/);
});

test("规格列表、重复排查和 AI 搜索应使用同一部门范围", () => {
  assert.match(tableSource, /params\.orgUnitId = props\.orgUnitId/);
  assert.match(tableSource, /detectSpecDuplicateGroups\(\{/);
  assert.match(tableSource, /:org-unit-id="orgUnitId"/);
  assert.match(semanticSource, /request\.orgUnitId = props\.orgUnitId/);
  assert.match(apiSource, /orgUnitId\?: number/);
});

test("管理员手工新增规格时应提交明确的业务归属部门", () => {
  assert.match(tableSource, /prop="businessOrgUnitId"/);
  assert.match(tableSource, /请选择所属部门/);
  assert.match(
    tableSource,
    /businessOrgUnitId: formData\.businessOrgUnitId \?\? undefined/
  );
});
