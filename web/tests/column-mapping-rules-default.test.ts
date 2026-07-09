import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const getRepositoryRoot = () => {
  const cwd = process.cwd();
  if (existsSync(resolve(cwd, "web/package.json"))) {
    return cwd;
  }

  const parent = resolve(cwd, "..");
  if (existsSync(resolve(parent, "web/package.json"))) {
    return parent;
  }

  return cwd;
};

const repositoryRoot = getRepositoryRoot();
const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(repositoryRoot, relativePath), "utf8");

test("列映射规则新增弹窗默认匹配模式应为相等", () => {
  const source = readProjectFile(
    "web/src/views/config/column-mapping-rules/index.vue"
  );

  assert.match(source, /matchMode:\s*ColumnMappingMatchMode\.Equals/);
  assert.match(source, /form\.matchMode = ColumnMappingMatchMode\.Equals/);
});

test("列映射规则页应为每个 Tab 保留独立搜索词且不再使用固定 320 宽下拉", () => {
  const source = readProjectFile(
    "web/src/views/config/column-mapping-rules/index.vue"
  );

  assert.doesNotMatch(source, /tableSelectWidth\s*=\s*320/);
  assert.doesNotMatch(source, /table-select--320/);
  assert.match(source, /const tabKeywords = reactive\(\{/);
  assert.match(source, /\[ColumnMappingTargetField\.Project\]: ""/);
  assert.match(source, /\[ColumnMappingTargetField\.Specification\]: ""/);
  assert.match(source, /\[ColumnMappingTargetField\.Acceptance\]: ""/);
  assert.match(source, /\[ColumnMappingTargetField\.Remark\]: ""/);
  assert.match(source, /v-model="tabKeywords\[target\.value\]"/);
  assert.match(source, /rule\.pattern\.toLowerCase\(\)\.includes\(keyword\)/);
  assert.match(source, /matchModeLabel\.includes\(keyword\)/);
  assert.match(
    source,
    /placeholder="搜索当前字段的匹配词 \/ 匹配模式 \/ 来源 \/ 客户"/
  );
  assert.match(source, /class="table-select"/);
});

test("列映射规则页优先级输入框应与列宽匹配，避免被挤压", () => {
  const source = readProjectFile(
    "web/src/views/config/column-mapping-rules/index.vue"
  );

  assert.match(
    source,
    /<el-table-column[\s\S]*label="优先级"[\s\S]*width="(?:140|min\(140px, calc\(100vw - 32px\)\)")/
  );
  assert.match(source, /<el-input-number[\s\S]*class="table-number-input"/);
  assert.match(source, /\.table-number-input\s*\{/);
  assert.match(source, /width:\s*100%;/);
});

test("列映射规则页应提供恢复内置默认词入口", () => {
  const source = readProjectFile(
    "web/src/views/config/column-mapping-rules/index.vue"
  );
  const apiSource = readProjectFile("web/src/api/column-mapping-rules.ts");

  assert.match(apiSource, /restoreColumnMappingRuleDefaults/);
  assert.match(apiSource, /\/restore-defaults/);
  assert.match(apiSource, /params:\s*targetField === undefined/);

  assert.match(source, /restoreColumnMappingRuleDefaults/);
  assert.match(source, /const restoreDefaults = async/);
  assert.match(source, /btn:column-mapping-rule:create/);
  assert.match(source, /恢复默认词/);
  assert.match(source, /重启只在某字段内置词全空时兜底补齐/);
  assert.match(source, /@click="restoreDefaults"/);
});
