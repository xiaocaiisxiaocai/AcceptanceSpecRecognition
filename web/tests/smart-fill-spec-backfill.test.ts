import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

test("smart-fill 应提供编辑值回填验收规格 API 封装", () => {
  const matchingApiSource = readProjectFile("src/api/matching.ts");

  assert.match(matchingApiSource, /SmartFillSpecBackfillRequest/);
  assert.match(matchingApiSource, /SmartFillSpecBackfillResponse/);
  assert.match(matchingApiSource, /spec-backfill/);
  assert.match(matchingApiSource, /backfillSmartFillSpecs/);
});

test("smart-fill 执行填充前应弹出编辑值回填确认框", () => {
  const smartFillSource = readProjectFile("src/views/smart-fill/index.vue");
  const batchPreviewTabsSource = readProjectFile(
    "src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const matchPreviewTableSource = readProjectFile(
    "src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(matchPreviewTableSource, /getEditedBackfillItems/);
  assert.match(batchPreviewTabsSource, /getAllEditedBackfillItems/);
  assert.match(smartFillSource, /回填验收规格/);
  assert.match(smartFillSource, /不回填，仅执行填充/);
  assert.match(smartFillSource, /确认回填并执行填充/);
  assert.match(smartFillSource, /backfillSmartFillSpecs/);
});

test("smart-fill 回填前应校验新增规格范围并透出真实错误", () => {
  const smartFillSource = readProjectFile("src/views/smart-fill/index.vue");

  assert.match(smartFillSource, /selected\.some\(item => item\.actionType === "create"\)/);
  assert.match(smartFillSource, /回填新增规格前，请先选择客户范围/);
  assert.match(
    smartFillSource,
    /ElMessage\.error\(getRequestErrorMessage\(error\) \|\| "回填或填充失败"\)/
  );
});

test("smart-fill 应缓存匹配范围并在执行回填时复用", () => {
  const smartFillSource = readProjectFile("src/views/smart-fill/index.vue");

  assert.match(smartFillSource, /const matchScope = ref<\{/);
  assert.match(smartFillSource, /const handleScopeChange = \(/);
  assert.match(smartFillSource, /matchConfigRef\.value\?\.getScope\(\) \?\? matchScope\.value/);
  assert.match(smartFillSource, /@scope-change="handleScopeChange"/);
});
