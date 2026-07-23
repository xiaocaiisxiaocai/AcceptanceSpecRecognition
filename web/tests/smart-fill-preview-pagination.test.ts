import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = (() => {
  const cwd = process.cwd();
  if (existsSync(resolve(cwd, "web/package.json"))) {
    return cwd;
  }

  return resolve(cwd, "..");
})();

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(repositoryRoot, relativePath), "utf8");

test("MatchPreviewTable 在大结果集场景下应使用分页后的数据源渲染表格", () => {
  const source = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(source, /const currentPage = ref\(1\);/);
  assert.match(source, /const pageSize = ref\(50\);/);
  assert.match(source, /const pageSizeOptions = \[20, 50, 100, 200\];/);
  assert.match(source, /const pagedFilteredItems = computed\(\(\) =>/);
  assert.match(source, /:items="pagedFilteredItems"/);
  const dataTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewDataTable.vue"
  );
  assert.match(dataTableSource, /<el-pagination/);
  assert.ok(
    !/:data="filteredItems"/.test(source),
    "大数据场景下不应再把整批 filteredItems 直接绑定给 el-table"
  );
});

test("MatchPreviewTable 应将 persistedSelections 预处理为 Map，避免大数据下重复线性查找", () => {
  const source = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(source, /const persistedStateMap = computed\(/);
  assert.match(source, /persistedStateMap\.value\.get\(rowIndex\)/);
  assert.ok(
    !/props\.persistedSelections\?\.find\(item => item\.rowIndex === rowIndex\)/.test(
      source
    ),
    "大结果集下不应继续对 persistedSelections 逐行执行 find"
  );
});
