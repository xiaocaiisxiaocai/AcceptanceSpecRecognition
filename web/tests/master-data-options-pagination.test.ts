import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readSource = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

const smartFill = readSource("web/src/views/smart-fill/index.vue");
const matchConfig = readSource(
  "web/src/views/smart-fill/components/MatchConfig.vue"
);
const dataImportTarget = readSource(
  "web/src/views/data-import/composables/useDataImportTarget.ts"
);

test("智能填充范围选项应使用全分页加载器并在销毁时取消", () => {
  assert.match(
    smartFill,
    /import \{ loadAllPagedItems \} from "@\/utils\/paged-options";/
  );
  assert.match(smartFill, /loadAllPagedItems\(/);
  assert.doesNotMatch(smartFill, /pageSize:\s*1000/);
  assert.match(smartFill, /scopeOptionsController\?\.abort\(\)/);
  assert.match(
    smartFill,
    /onBeforeUnmount\(\(\) => \{[\s\S]*scopeOptionsController\?\.abort\(\)/
  );
});

test("匹配配置主数据应使用全分页加载器并在销毁时取消", () => {
  assert.match(
    matchConfig,
    /import \{ loadAllPagedItems \} from "@\/utils\/paged-options";/
  );
  assert.ok((matchConfig.match(/loadAllPagedItems\(/g) ?? []).length >= 3);
  assert.doesNotMatch(matchConfig, /pageSize:\s*(?:100|1000)/);
  assert.match(matchConfig, /onBeforeUnmount/);
  assert.match(matchConfig, /\.abort\(\)/);
});

test("数据导入目标主数据应使用全分页加载器并在作用域销毁时取消", () => {
  assert.match(
    dataImportTarget,
    /import \{ loadAllPagedItems \} from "@\/utils\/paged-options";/
  );
  assert.ok((dataImportTarget.match(/loadAllPagedItems\(/g) ?? []).length >= 3);
  assert.doesNotMatch(dataImportTarget, /pageSize:\s*(?:100|1000)/);
  assert.match(dataImportTarget, /onScopeDispose/);
  assert.match(dataImportTarget, /\.abort\(\)/);
});

test("三个主数据列表 API 应透传 AbortSignal", () => {
  for (const path of [
    "web/src/api/customer.ts",
    "web/src/api/process.ts",
    "web/src/api/machine-model.ts"
  ]) {
    const source = readSource(path);
    assert.match(source, /options\?:\s*PagedListRequestOptions/);
    assert.match(source, /signal:\s*options\?\.signal/);
  }
});
