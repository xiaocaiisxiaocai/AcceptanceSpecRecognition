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

const getTableColumnFragment = (source: string, label: string) => {
  const labelIndex = source.indexOf(`label="${label}"`);
  assert.notEqual(labelIndex, -1, `未找到 ${label} 列`);

  const startIndex = source.lastIndexOf("<el-table-column", labelIndex);
  const closingTag = "</el-table-column>";
  const endIndex = source.indexOf(closingTag, labelIndex);
  assert.notEqual(startIndex, -1, `未找到 ${label} 列起始标签`);
  assert.notEqual(endIndex, -1, `未找到 ${label} 列结束标签`);

  return source.slice(startIndex, endIndex + closingTag.length);
};

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
  const priorityColumn = getTableColumnFragment(source, "优先级");

  assert.match(
    priorityColumn,
    /width="(?:140|min\(140px, calc\(100vw - 32px\)\))"/
  );
  assert.match(priorityColumn, /class="table-number-input"/);
  assert.match(source, /\.table-number-input\s*\{/);
  assert.match(source, /width:\s*100%;/);
});

test("优先级列宽守卫不得借用相邻列的宽度", () => {
  const source = `
    <el-table-column label="客户域" width="min(140px, calc(100vw - 32px))">
    </el-table-column>
    <el-table-column label="优先级" width="90">
      <el-input-number class="table-number-input" />
    </el-table-column>
  `;

  const priorityColumn = getTableColumnFragment(source, "优先级");

  assert.doesNotMatch(
    priorityColumn,
    /width="(?:140|min\(140px, calc\(100vw - 32px\)\))"/
  );
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

test("列映射规则客户域应显示具体客户并通过下拉框选择", () => {
  const source = readProjectFile(
    "web/src/views/config/column-mapping-rules/index.vue"
  );
  const customerFormStart = source.indexOf('<el-form-item label="客户域">');
  const customerFormEnd = source.indexOf("</el-form-item>", customerFormStart);
  assert.notEqual(customerFormStart, -1, "未找到客户域表单项");
  assert.notEqual(customerFormEnd, -1, "未找到客户域表单项结束标签");
  const customerForm = source.slice(
    customerFormStart,
    customerFormEnd + "</el-form-item>".length
  );

  assert.match(source, /getCustomerList/);
  assert.match(source, /loadAllPagedItems/);
  assert.match(source, /formatCustomerScope\(row\.customerId\)/);
  assert.match(source, /formatCustomerScope\(customer\.id\)/);
  assert.match(source, /未知客户（ID: \$\{customerId\}）/);
  assert.match(
    customerForm,
    /<el-select[\s\S]*?v-model="form\.customerId"[\s\S]*?filterable[\s\S]*?clearable/
  );
  assert.doesNotMatch(customerForm, /<el-input-number/);
});
