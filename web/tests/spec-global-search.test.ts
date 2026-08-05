import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const specTableSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/base-data/specs/components/SpecTable.vue"
  ),
  "utf8"
);

test("验收规格应提供全局搜索入口", () => {
  assert.match(specTableSource, /globalSearch/);
  assert.match(specTableSource, /全局搜索/);
  assert.match(specTableSource, /项目\/规格\/验收标准\/备注/);
});

test("验收规格全局搜索请求不应携带当前分组条件", () => {
  assert.match(
    specTableSource,
    /if\s*\(\s*queryParams\.globalSearch\s*\)\s*\{/
  );
  assert.match(specTableSource, /return params;/);
  assert.match(specTableSource, /params\.customerId = props\.customerId/);
  assert.match(
    specTableSource,
    /params\.machineModelId = props\.machineModelId/
  );
  assert.match(specTableSource, /params\.processId = props\.processId/);
});

test("验收规格应通过面包屑展示来源范围并移除冗余来源列", () => {
  assert.match(specTableSource, /const scopeBreadcrumbItems = computed/);
  assert.match(specTableSource, /<el-breadcrumb/);
  assert.match(
    specTableSource,
    /v-for="\((item,\s*index)\) in scopeBreadcrumbItems"/
  );
  assert.doesNotMatch(specTableSource, /<el-table-column prop="id"/);
  assert.doesNotMatch(specTableSource, /prop="customerName"/);
  assert.doesNotMatch(specTableSource, /prop="machineModelName"/);
  assert.doesNotMatch(specTableSource, /prop="processName"/);
  assert.match(specTableSource, /prop="project"[\s\S]{0,100}width="140"/);
});

test("规格内容应在列表中保留换行并多行展示", () => {
  assert.match(
    specTableSource,
    /prop="specification"[\s\S]{0,300}class="specification-multiline"/
  );
  assert.match(
    specTableSource,
    /\.specification-multiline\s*\{[\s\S]*white-space:\s*pre-wrap/
  );
  assert.doesNotMatch(
    specTableSource,
    /prop="specification"[\s\S]{0,300}class="line-clamp-1"/
  );
});

test("验收规格列表只允许最新请求更新页面状态", () => {
  assert.match(specTableSource, /let latestLoadRequestId = 0/);
  assert.match(specTableSource, /const requestId = \+\+latestLoadRequestId/);
  assert.match(
    specTableSource,
    /if\s*\(\s*requestId !== latestLoadRequestId\s*\)\s*return/
  );
  assert.match(
    specTableSource,
    /if\s*\(\s*requestId === latestLoadRequestId\s*\)[\s\S]{0,120}loading\.value = false/
  );
});

test("验收规格分页应保留五百条并移除一千条", () => {
  assert.match(specTableSource, /:page-sizes="\[100, 200, 500\]"/);
  assert.doesNotMatch(specTableSource, /:page-sizes="[^"]*1000/);
});

test("验收规格列表应展示更新时间并对历史数据回退导入时间", () => {
  assert.match(
    specTableSource,
    /<el-table-column[^>]*label="更新时间"[^>]*width="180"/
  );
  assert.match(
    specTableSource,
    /formatApiUtcDateTime\(row\.updatedAt \?\? row\.importedAt\)/
  );
});

test("验收规格列表和详情应展示引用次数", () => {
  assert.match(
    specTableSource,
    /<el-table-column[\s\S]{0,160}prop="referenceCount"[\s\S]{0,160}label="引用次数"/
  );
  assert.match(
    specTableSource,
    /<el-descriptions-item label="引用次数">[\s\S]{0,80}detailData\.referenceCount/
  );
});

test("验收规格批量删除应要求输入删除数量并防止选择变化后误删", () => {
  const handlerStart = specTableSource.indexOf(
    "const handleBatchDelete = async () =>"
  );
  const handlerEnd = specTableSource.indexOf(
    "const handleInspectDuplicates = async () =>"
  );
  const handlerSource = specTableSource.slice(handlerStart, handlerEnd);

  assert.ok(handlerStart >= 0 && handlerEnd > handlerStart);
  assert.match(handlerSource, /永久删除/);
  assert.match(handlerSource, /ElMessageBox\.prompt/);
  assert.match(handlerSource, /value\.trim\(\) === String\(deleteCount\)/);
  assert.match(handlerSource, /currentIds/);
  assert.match(handlerSource, /选择内容已变化/);
  assert.ok(
    handlerSource.indexOf("ElMessageBox.prompt") <
      handlerSource.indexOf("batchDeleteSpecs(ids)")
  );
});
