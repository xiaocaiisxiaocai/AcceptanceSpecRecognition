import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

const readSource = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

const matchConfigSource = readSource(
  "web/src/views/smart-fill/components/MatchConfig.vue"
);
const semanticSearchSource = readSource(
  "web/src/views/base-data/specs/components/SpecSemanticSearchDialog.vue"
);
const differenceDialogSource = readSource(
  "web/src/views/data-import/components/DataImportDifferenceConfirmDialog.vue"
);

test("智能填充应把技术参数收进可访问的高级设置", () => {
  assert.match(matchConfigSource, /const showMatchingAdvanced = ref\(false\)/);
  assert.match(matchConfigSource, /高级匹配参数/);
  assert.match(matchConfigSource, /aria-controls="matching-advanced-options"/);
  assert.match(matchConfigSource, /class="matching-strategy-summary"/);
  assert.match(matchConfigSource, /<el-col :xs="24" :md="12">/);
  assert.match(matchConfigSource, /<button[\s\S]*同步 LLM 复核/);
});

test("AI 搜索应使用业务化参数名称并说明当前筛选策略", () => {
  assert.match(semanticSearchSource, /每条最多候选数/);
  assert.match(semanticSearchSource, /最低相似度/);
  assert.match(semanticSearchSource, /当前设置：每条最多返回/);
  assert.doesNotMatch(semanticSearchSource, /<span>TopK<\/span>/);
  assert.doesNotMatch(semanticSearchSource, /<span>最小分数<\/span>/);
});

test("导入差异确认应展示后端已返回的 LLM 判断说明", () => {
  assert.match(differenceDialogSource, /v-if="item\.reviewCommentary"/);
  assert.match(differenceDialogSource, /判断说明/);
  assert.match(differenceDialogSource, /\{\{ item\.reviewCommentary \}\}/);
});
