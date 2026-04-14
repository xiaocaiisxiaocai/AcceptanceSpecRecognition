import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  getLlmEquivalenceDifferenceTone,
  isLlmEquivalenceDecisionRisk,
  isLlmEquivalenceHintOnly
} from "../src/views/smart-fill/components/scoreDetail.llmEquivalence.ts";

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

test("等价表达与格式差异应被识别为提示型，不升级为决策风险", () => {
  assert.equal(
    isLlmEquivalenceHintOnly({
      verdict: "equivalent",
      reasonType: "equivalent_expression",
      confidence: 0.91
    }),
    true
  );
  assert.equal(
    isLlmEquivalenceHintOnly({
      verdict: "equivalent",
      reasonType: "format_only",
      confidence: 0.82
    }),
    true
  );
  assert.equal(
    isLlmEquivalenceDecisionRisk({
      verdict: "equivalent",
      reasonType: "equivalent_expression",
      confidence: 0.91
    }),
    false
  );
  assert.equal(
    getLlmEquivalenceDifferenceTone({
      verdict: "equivalent",
      reasonType: "equivalent_expression",
      confidence: 0.91
    }),
    "hint"
  );
});

test("语义差异与不确定应被识别为决策型风险", () => {
  assert.equal(
    isLlmEquivalenceDecisionRisk({
      verdict: "different",
      reasonType: "semantic_difference",
      confidence: 0.76
    }),
    true
  );
  assert.equal(
    isLlmEquivalenceDecisionRisk({
      verdict: "uncertain",
      reasonType: "uncertain",
      confidence: 0.36
    }),
    true
  );
  assert.equal(
    getLlmEquivalenceDifferenceTone({
      verdict: "different",
      reasonType: "semantic_difference",
      confidence: 0.76
    }),
    "decision"
  );
});

test("匹配 API 与执行填充应接收并透传 AI 等价裁决字段", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(matchingApiSource, /export interface LlmEquivalenceResult \{/);
  assert.match(matchingApiSource, /llmEquivalence\?: LlmEquivalenceResult;/);
  assert.match(matchingApiSource, /llmEquivalenceVerdict\?: LlmEquivalenceVerdict;/);
  assert.match(
    matchingApiSource,
    /decision\?: "autoApply" \| "manualReview" \| "reject";/
  );
  assert.match(smartFillPageSource, /llmEquivalenceVerdict:\s*s\.llmEquivalenceVerdict/);
  assert.match(smartFillPageSource, /decision:\s*s\.decision/);
  assert.match(previewTableSource, /decision:\s*item\.bestMatch\.decision/);
});

test("详情区域应展示 AI 裁决与提示型\/决策型差异说明", () => {
  const decisionSummarySource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue"
  );
  const diffSectionSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailDiffSection.vue"
  );
  const equivalenceHelperSource = readProjectFile(
    "web/src/views/smart-fill/components/scoreDetail.llmEquivalence.ts"
  );

  assert.match(decisionSummarySource, /AI 等价裁决/);
  assert.match(diffSectionSource, /getLlmEquivalenceDifferenceToneText/);
  assert.match(equivalenceHelperSource, /提示型差异/);
  assert.match(equivalenceHelperSource, /决策型风险/);
});

test("主表只应将 autoApply 且 high 的结果视为高置信可直填", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(
    previewTableSource,
    /const isAutoApply = \(item: MatchPreviewItem\) => getDecision\(item\) === "autoApply";/
  );
  assert.match(
    previewTableSource,
    /isAutoApply\(item\)\s*&&\s*item\.confidenceLevel === "high"/
  );
});
