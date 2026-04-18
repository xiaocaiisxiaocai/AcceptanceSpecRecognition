import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  getLlmEquivalenceDifferenceTone,
  shouldHideInlineLlmEquivalenceSummary,
  isLlmEquivalenceDecisionRisk,
  isLlmEquivalenceHintOnly
} from "../src/views/smart-fill/components/scoreDetail.llmEquivalence.ts";
import {
  getSmartFillDecisionSummaryState,
  getSmartFillTableState,
  getSelectionModeDescription,
  getSelectionModeText,
  getSortedScoreDetails,
  applyMatchLlmStreamEventToPreviewItem,
  applyMatchLlmStreamDisconnectToPreviewItem,
  shouldStreamMatchReview
} from "../src/views/smart-fill/components/scoreDetail.formatters.ts";
import type { MatchPreviewItem } from "../src/api/matching.ts";

const createPreviewItem = (
  overrides: Partial<MatchPreviewItem> = {}
): MatchPreviewItem => ({
  rowIndex: 0,
  sourceProject: "项目A",
  sourceSpecification: "100V",
  hasMatch: true,
  confidenceLevel: "high",
  bestMatch: {
    specId: 1001,
    project: "项目A",
    specification: "100V",
    acceptance: "通过",
    remark: "备注",
    score: 0.98,
    embeddingScore: 0.95,
    scoreDetails: {
      Final: 0.98,
      Embedding: 0.95
    },
    decision: "manualReview",
    selectionMode: "embeddingTop1",
    selectionSummary: "本地 Top1 命中当前候选。",
    evidenceSummary: [],
    conflictSummary: [],
    issues: [],
    entities: [],
    topCandidates: [],
    recalledCandidateCount: 2,
    isAmbiguous: false,
    llmEquivalence: {
      verdict: "equivalent",
      reasonType: "equivalent_expression",
      confidence: 0.93,
      reason: "仅是等价表达"
    }
  },
  ...overrides
});

test("最终 decision 为人工确认时，提示型 AI 等价不应显示为可直接填充或低风险", () => {
  const item = createPreviewItem();

  const tableState = getSmartFillTableState(item);
  const summaryState = getSmartFillDecisionSummaryState(item, {
    sourceBestRowCount: 0
  });

  assert.equal(tableState.fillRecommendation, "review");
  assert.equal(tableState.reviewStatus, "manual");
  assert.equal(summaryState.recommendation.title, "需要确认");
  assert.equal(summaryState.actionSuggestion, "确认后再填充");
  assert.equal(summaryState.riskLevel.label, "中");
});

test("流式复核条件必须与后端门禁一致", () => {
  const manualAmbiguous = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      isAmbiguous: true
    }
  });
  const manualDifferent = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      llmEquivalence: {
        verdict: "different",
        reasonType: "semantic_difference",
        confidence: 0.74
      }
    }
  });
  const manualHintOnly = createPreviewItem();
  const autoApply = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "autoApply",
      isAmbiguous: true
    }
  });

  assert.equal(shouldStreamMatchReview(manualAmbiguous.bestMatch), true);
  assert.equal(shouldStreamMatchReview(manualDifferent.bestMatch), true);
  assert.equal(shouldStreamMatchReview(manualHintOnly.bestMatch), false);
  assert.equal(shouldStreamMatchReview(autoApply.bestMatch), false);
});

test("review.done 事件应完整回写复核结果字段", () => {
  const item = createPreviewItem({
    llmReviewStage: "streaming",
    llmReviewDraft: "正在复核"
  });

  applyMatchLlmStreamEventToPreviewItem(item, "review.done", {
    rowIndex: item.rowIndex,
    decision: "autoApply",
    score: 0.91,
    reason: "AI 判定可直接采用",
    commentary: "实体一致，差异仅为格式",
    reviewApprovalToken: "token-123"
  });

  assert.equal(item.bestMatch?.decision, "autoApply");
  assert.equal(item.bestMatch?.reviewScore, 0.91);
  assert.equal(item.bestMatch?.reviewReason, "AI 判定可直接采用");
  assert.equal(item.bestMatch?.reviewCommentary, "实体一致，差异仅为格式");
  assert.equal(item.bestMatch?.reviewApprovalToken, "token-123");
  assert.equal(item.llmReviewStage, "done");
  assert.equal(item.llmReviewDraft, "");
  assert.equal(item.llmReviewError, undefined);
});

test("review.error 事件应回写 decision 和错误信息，避免状态漂移", () => {
  const item = createPreviewItem({
    llmReviewStage: "streaming",
    llmReviewDraft: "正在复核"
  });

  applyMatchLlmStreamEventToPreviewItem(item, "review.error", {
    rowIndex: item.rowIndex,
    decision: "manualReview",
    message: "LLM 复核超时"
  });

  assert.equal(item.bestMatch?.decision, "manualReview");
  assert.equal(item.llmReviewStage, "error");
  assert.equal(item.llmReviewError, "LLM 复核超时");
  assert.equal(item.llmReviewDraft, "");
});

test("流式连接中断时，应把 streaming 行收口为人工确认错误态", () => {
  const item = createPreviewItem({
    llmReviewStage: "streaming",
    llmReviewDraft: "正在复核"
  });

  applyMatchLlmStreamDisconnectToPreviewItem(
    item,
    "LLM流式输出中断，已转为人工确认"
  );

  assert.equal(item.bestMatch?.decision, "manualReview");
  assert.equal(item.llmReviewStage, "error");
  assert.equal(item.llmReviewDraft, "");
  assert.equal(item.llmReviewError, "LLM流式输出中断，已转为人工确认");
});

test("流式连接中断时，也应把 waiting/pending 行收口，避免一直停留在待复核", () => {
  const waitingItem = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      isAmbiguous: true
    }
  });
  const pendingItem = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      llmEquivalence: {
        verdict: "different",
        reasonType: "semantic_difference",
        confidence: 0.74
      }
    }
  });

  assert.equal(
    getSmartFillTableState(waitingItem, { llmStreaming: true }).reviewStatus,
    "waiting"
  );
  assert.equal(
    getSmartFillTableState(pendingItem, { llmStreaming: false }).reviewStatus,
    "pending"
  );

  applyMatchLlmStreamDisconnectToPreviewItem(waitingItem, "连接中断");
  applyMatchLlmStreamDisconnectToPreviewItem(pendingItem, "连接中断");

  assert.equal(waitingItem.bestMatch?.decision, "manualReview");
  assert.equal(waitingItem.llmReviewStage, "error");
  assert.equal(waitingItem.llmReviewError, "连接中断");
  assert.equal(pendingItem.bestMatch?.decision, "manualReview");
  assert.equal(pendingItem.llmReviewStage, "error");
  assert.equal(pendingItem.llmReviewError, "连接中断");
});

test("主表与详情区不应再用本地风险信号覆盖 authoritative decision", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const decisionSummarySource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue"
  );

  assert.doesNotMatch(
    previewTableSource,
    /if \(hasCustomerVisibleRisk\(item\)\) return "review";/
  );
  assert.match(previewTableSource, /return tableState\.fillRecommendation;/);
  assert.doesNotMatch(
    decisionSummarySource,
    /const hasCustomerVisibleDifference = computed\(/
  );
  assert.doesNotMatch(
    decisionSummarySource,
    /description: "存在差异，请先确认"/
  );
  assert.doesNotMatch(
    decisionSummarySource,
    /return "核对差异后再填充";/
  );
});

test("llm-stream 非2xx或无 body 时应先收口中断行，且 filterEmptySourceRows helper 不应重复声明", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const helperCount = (
    smartFillPageSource.match(/const getEffectiveFilterEmptySourceRows =/g) ?? []
  ).length;

  assert.equal(helperCount, 1);
  assert.match(
    smartFillPageSource,
    /if \(!response\.ok \|\| !response\.body\) \{[\s\S]*finalizeInterruptedLlmStreamRows\([\s\S]*llmStreamController\.value === controller/
  );
});

test("智能填充前端不应再暴露 hasHardConflict 或 HardConflictPenalty 遗留字段", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const bestMatchSectionSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue"
  );
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/scoreDetail.formatters.ts"
  );

  assert.doesNotMatch(matchingApiSource, /hasHardConflict\??:/);
  assert.doesNotMatch(previewTableSource, /hasHardConflict/);
  assert.doesNotMatch(bestMatchSectionSource, /hasHardConflict/);
  assert.doesNotMatch(formatterSource, /HardConflictPenalty/);
  assert.doesNotMatch(formatterSource, /hasHardConflict/);
});

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

const getInterfaceBlock = (source: string, interfaceName: string) => {
  const match = source.match(
    new RegExp(`export interface ${interfaceName} \\{[\\s\\S]*?\\n\\}`, "m")
  );
  assert.ok(match, `应能定位 ${interfaceName} 接口定义`);
  return match[0];
};

const getLlmStreamPayloadBlock = (smartFillPageSource: string) => {
  const match = smartFillPageSource.match(
    /const startLlmStream = async \(\) => \{[\s\S]*?const payload = createMatchLlmStreamRequest\(\{[\s\S]*?\}\);[\s\S]*?requestMatchLlmStream\(payload,\s*controller\.signal\)/
  );
  assert.ok(match, "应能定位 startLlmStream 中发送 llm-stream 的 payload 代码块");
  return match[0];
};

test("批量预览 Tab 应维护本地可切换状态，而不是把只读 computed 绑定给 v-model", () => {
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );

  assert.match(previewTabsSource, /import\s+\{\s*computed,\s*ref,\s*watch\s*\}\s+from "vue";/);
  assert.match(previewTabsSource, /const activeTab = ref\(/);
  assert.match(
    previewTabsSource,
    /watch\(\s*\(\)\s*=>\s*props\.results,\s*\(results\)\s*=>[\s\S]*activeTab\.value/
  );
  assert.doesNotMatch(previewTabsSource, /const activeTab = computed\(/);
});

test("匹配配置文案不应再暗示达到阈值就会默认选中", () => {
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.doesNotMatch(matchConfigSource, /默认选中/);
  assert.doesNotMatch(matchConfigSource, /自动勾选仍以高置信阈值为准/);
  assert.match(matchConfigSource, /高置信阈值只用于结果分层展示/);
  assert.match(matchConfigSource, /不决定自动采用/);
});

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

test("列表页仅应隐藏 100% 且属于直达等价说明的 AI 提示", () => {
  assert.equal(
    shouldHideInlineLlmEquivalenceSummary(
      {
        verdict: "equivalent",
        reasonType: "equivalent_expression",
        confidence: 1,
        reason: "项目与规格文本完全一致，已直接视为等价"
      },
      1
    ),
    true
  );
  assert.equal(
    shouldHideInlineLlmEquivalenceSummary(
      {
        verdict: "equivalent",
        reasonType: "equivalent_expression",
        confidence: 1,
        reason: "项目与规格文本完全一致，已直接视为等价"
      },
      0.99
    ),
    false
  );
  assert.equal(
    shouldHideInlineLlmEquivalenceSummary(
      {
        verdict: "equivalent",
        reasonType: "equivalent_expression",
        confidence: 0.94,
        reason: "仅是等价表达"
      },
      1
    ),
    false
  );

  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(
    previewTableSource,
    /!shouldHideInlineLlmEquivalenceSummary\(row\.bestMatch\.llmEquivalence,\s*row\.bestMatch\.score\)/
  );
  assert.equal(
    (
      previewTableSource.match(
        /!shouldHideInlineLlmEquivalenceSummary\(row\.bestMatch\.llmEquivalence,\s*row\.bestMatch\.score\)/g
      ) ?? []
    ).length,
    1
  );
});

test("说明列应只保留异常原因，不再重复展示最佳匹配中的证据与 AI 摘要", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const reasonColumnMatch = previewTableSource.match(
    /<!-- 不匹配原因 \/ 复核说明 -->[\s\S]*?<!-- 操作 -->/
  );

  assert.ok(reasonColumnMatch, "应能定位说明列代码块");
  const reasonColumnBlock = reasonColumnMatch[0];

  assert.match(previewTableSource, /const hasReasonColumn = computed\(/);
  assert.match(previewTableSource, /!!item\.noMatchReason/);
  assert.match(previewTableSource, /!!item\.bestMatch\?\.conflictSummary\?\.length/);
  assert.match(previewTableSource, /!!item\.llmReviewError/);
  assert.doesNotMatch(previewTableSource, /!!item\.bestMatch\?\.issues\?\.length/);
  assert.doesNotMatch(previewTableSource, /!!item\.bestMatch\?\.evidenceSummary\?\.length/);
  assert.doesNotMatch(previewTableSource, /!!item\.bestMatch\?\.llmEquivalence/);

  assert.doesNotMatch(reasonColumnBlock, /问题：/);
  assert.doesNotMatch(reasonColumnBlock, /建议：/);
  assert.doesNotMatch(reasonColumnBlock, /证据：/);
  assert.doesNotMatch(reasonColumnBlock, /AI 裁决：/);
  assert.match(reasonColumnBlock, /label="异常\/原因"/);
  assert.doesNotMatch(reasonColumnBlock, /label="说明"/);
  assert.match(reasonColumnBlock, /复核异常：/);
  assert.match(reasonColumnBlock, /冲突：/);
});

test("顶部筛选应区分 100%可填充 与低于100%的可填充，行内仍统一显示可直接填充", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(previewTableSource, /const isExactFillable = \(item: MatchPreviewItem\) =>/);
  assert.match(previewTableSource, /const isPartialFillable = \(item: MatchPreviewItem\) =>/);
  assert.match(previewTableSource, /item\.bestMatch\?\.selectionMode === "exactShortcut"/);
  assert.match(previewTableSource, /item\.bestMatch\.selectionMode !== "exactShortcut"/);
  assert.doesNotMatch(previewTableSource, /item\.bestMatch\?\.score === 1/);
  assert.match(previewTableSource, /return "可直接填充";/);
  assert.match(previewTableSource, /100%精确直达 \(\{\{ stats\.exactFillable \}\}\)/);
  assert.match(previewTableSource, /AI\/普通可填充 \(\{\{ stats\.partialFillable \}\}\)/);
  assert.doesNotMatch(previewTableSource, /可填充 \(\{\{ stats\.fillable \}\}\)/);
});

test("selectionMode 文案应明确区分精确直达、本地 Top1 与 AI 改选", () => {
  assert.equal(getSelectionModeText("exactShortcut"), "100%精确直达");
  assert.equal(getSelectionModeText("embeddingTop1"), "本地 Top1");
  assert.equal(getSelectionModeText("aiRerank"), "AI 改选");
  assert.match(getSelectionModeDescription("exactShortcut"), /未走 AI 改选/);
  assert.match(getSelectionModeDescription("embeddingTop1"), /本地召回 Top1/);
  assert.match(getSelectionModeDescription("aiRerank"), /AI/);

  const bestMatchSectionSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue"
  );
  const candidateListSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailCandidateList.vue"
  );

  assert.match(bestMatchSectionSource, /选定方式/);
  assert.match(bestMatchSectionSource, /选定摘要/);
  assert.match(candidateListSource, /选定摘要/);
  assert.match(candidateListSource, /getSelectionModeText/);
});

test("匹配 API 应暴露 AI 等价裁决结果，但执行填充不再透传旧客户端决策字段", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(matchingApiSource, /export interface LlmEquivalenceResult \{/);
  assert.match(matchingApiSource, /export type MatchSelectionMode =/);
  assert.match(matchingApiSource, /llmEquivalence\?: LlmEquivalenceResult;/);
  assert.match(matchingApiSource, /selectionMode\?: MatchSelectionMode;/);
  assert.match(matchingApiSource, /selectionSummary\?: string;/);
  assert.doesNotMatch(fillMappingBlock, /matchScore\?:/);
  assert.doesNotMatch(fillMappingBlock, /llmReviewScore\?:/);
  assert.doesNotMatch(fillMappingBlock, /llmEquivalenceVerdict\?:/);
  assert.doesNotMatch(previewTabsSource, /llmReviewScore\?: number;/);
  assert.doesNotMatch(previewTableSource, /llmReviewScore\?: number;/);
  assert.doesNotMatch(previewTableSource, /llmReviewScore:/);
  assert.doesNotMatch(smartFillPageSource, /llmReviewScore:\s*s\.llmReviewScore/);
  assert.doesNotMatch(smartFillPageSource, /llmReviewScore\?: number;/);
  assert.doesNotMatch(smartFillPageSource, /decision:\s*s\.decision/);
  assert.match(
    previewTableSource,
    /item\.bestMatch\?\.decision \?\? "manualReview"/
  );
});

test("AI 复核放行令牌应透传到执行填充请求，避免客户端布尔值代替服务端凭据", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(fillMappingBlock, /reviewApprovalToken\?: string;/);
  assert.match(previewTabsSource, /reviewApprovalToken\?: string;/);
  assert.match(previewTableSource, /reviewApprovalToken\?: string;/);
  assert.match(
    previewTableSource,
    /reviewApprovalToken:\s*item\.bestMatch\?\.reviewApprovalToken/
  );
  assert.match(smartFillPageSource, /reviewApprovalToken:\s*s\.reviewApprovalToken/);
});

test("智能填充执行请求应透传本次导出覆盖值，而不是只发送规格ID", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(fillMappingBlock, /overrideAcceptance\?: string;/);
  assert.match(fillMappingBlock, /overrideRemark\?: string;/);
  assert.match(previewTabsSource, /overrideAcceptance\?: string;/);
  assert.match(previewTabsSource, /overrideRemark\?: string;/);
  assert.match(previewTableSource, /overrideAcceptance\?: string;/);
  assert.match(previewTableSource, /overrideRemark\?: string;/);
  assert.match(previewTableSource, /overrideAcceptance:\s*.*overrideAcceptance/);
  assert.match(previewTableSource, /overrideRemark:\s*.*overrideRemark/);
  assert.match(smartFillPageSource, /overrideAcceptance:\s*s\.overrideAcceptance/);
  assert.match(smartFillPageSource, /overrideRemark:\s*s\.overrideRemark/);
});

test("智能填充预览页应提供编辑弹窗、保存并采用和已编辑标记", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(previewTableSource, />\s*编辑\s*<\/el-button>/);
  assert.match(previewTableSource, /保存并采用/);
  assert.match(previewTableSource, /仅本次导出使用/);
  assert.match(previewTableSource, /已编辑/);
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

test("主表应以后端 decision 加 confidenceLevel 判定高置信直填，且流式复核请求应走类型化 API", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const llmStreamPayloadBlock = getLlmStreamPayloadBlock(smartFillPageSource);
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.match(
    previewTableSource,
    /const isAutoApply = \(item: MatchPreviewItem\) => getDecision\(item\) === "autoApply";/
  );
  assert.match(previewTableSource, /const isHighConfidence = \(item: MatchPreviewItem\) =>/);
  assert.match(
    previewTableSource,
    /isAutoApply\(item\)\s*&&[\s\S]*item\.confidenceLevel === "high"/
  );
  assert.doesNotMatch(
    previewTableSource,
    /const isHighConfidence = \(item: MatchPreviewItem\) =>[\s\S]*llmEquivalence[\s\S]*const requiresReview/
  );
  assert.doesNotMatch(
    smartFillPageSource,
    /const shouldStreamReview = \(item: MatchPreviewItem\) =>[\s\S]*?llmEquivalence/
  );
  assert.doesNotMatch(smartFillPageSource, /authorizedFetch\(/);
  assert.match(matchingApiSource, /export interface MatchLlmStreamRequest \{/);
  assert.match(matchingApiSource, /export type MatchLlmStreamEvent =/);
  assert.match(matchingApiSource, /export const createMatchLlmStreamRequest = \(/);
  assert.match(matchingApiSource, /export const requestMatchLlmStream = (async )?\(/);
  assert.match(smartFillPageSource, /requestMatchLlmStream,\s*createMatchLlmStreamRequest/);
  assert.doesNotMatch(matchingApiSource, /MatchingStrategy/);
  assert.doesNotMatch(matchingApiSource, /matchingStrategy/);
  assert.match(
    llmStreamPayloadBlock,
    /const payload = createMatchLlmStreamRequest\(\{[\s\S]*customerId:\s*scope\.customerId,[\s\S]*processId:\s*scope\.processId,[\s\S]*machineModelId:\s*scope\.machineModelId,[\s\S]*config:\s*matchConfig\.value/
  );
});

test("预览表应在 decision、token、review stage 变化后同步选择状态，避免 stale selection", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.match(previewTableSource, /const selectionSyncKey = computed\(/);
  assert.match(
    previewTableSource,
    /item\.bestMatch\?\.decision[\s\S]*item\.bestMatch\?\.reviewApprovalToken[\s\S]*item\.llmReviewStage/
  );
  assert.match(previewTableSource, /const syncSelectionsWithItems = \(\) => \{/);
  assert.match(
    previewTableSource,
    /watch\(\s*selectionSyncKey,\s*\(\)\s*=>\s*syncSelectionsWithItems\(\),\s*\{\s*immediate:\s*true\s*\}\s*\)/
  );
});

test("SSE 事件缺少 tableIndex 时应直接丢弃，不能再跨表按 rowIndex 回退匹配", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(
    smartFillPageSource,
    /if \(data\.tableIndex === undefined \|\| data\.tableIndex === null\) \{\s*return;\s*\}/
  );
  assert.match(
    smartFillPageSource,
    /const tableResult = batchPreviewResults\.value\.find\(\s*tableResult => tableResult\.tableIndex === data\.tableIndex\s*\)/
  );
});

test("批量链路应让表级 filterEmptySourceRows 回退到全局配置", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(
    smartFillPageSource,
    /const getEffectiveFilterEmptySourceRows = \(\s*tableConfig:\s*\{[\s\S]*?filterEmptySourceRows\?: boolean;[\s\S]*?\}\s*\) =>[\s\S]*tableConfig\.filterEmptySourceRows \?\? matchConfig\.value\.filterEmptySourceRows \?\? true/
  );
  assert.match(
    smartFillPageSource,
    /filterEmptySourceRows:\s*getEffectiveFilterEmptySourceRows\(t\)/
  );
  assert.match(
    smartFillPageSource,
    /filterEmptySourceRows:\s*getEffectiveFilterEmptySourceRows\(config\)/
  );
});

test("智能填充前端不应再暴露旧的 suggestion 配置或结果字段", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.doesNotMatch(matchingApiSource, /useLlmSuggestion/);
  assert.doesNotMatch(matchingApiSource, /suggestNoMatchRows/);
  assert.doesNotMatch(matchingApiSource, /llmSuggestionScoreThreshold/);
  assert.doesNotMatch(matchingApiSource, /llmSuggestion\?:/);
  assert.doesNotMatch(matchingApiSource, /llmSuggestionDraft\?:/);
  assert.doesNotMatch(matchingApiSource, /llmSuggestionError\?:/);
  assert.doesNotMatch(matchingApiSource, /useLlmReview\?:/);
  assert.doesNotMatch(matchConfigSource, /useLlmSuggestion/);
  assert.doesNotMatch(matchConfigSource, /useLlmReview/);
});

test("智能填充前端应清理旧的 llmScore 及相关展示字段，统一使用 authoritative 信息", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const bestMatchSectionSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue"
  );
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/scoreDetail.formatters.ts"
  );
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.doesNotMatch(matchingApiSource, /\bllmScore\??:/);
  assert.doesNotMatch(matchingApiSource, /\bllmReason\??:/);
  assert.doesNotMatch(matchingApiSource, /\bllmCommentary\??:/);
  assert.doesNotMatch(matchingApiSource, /\bisLlmReviewed\??:/);

  assert.doesNotMatch(previewTableSource, /LLM_REVIEW_PASS_THRESHOLD/);
  assert.doesNotMatch(previewTableSource, /llmScore/);
  assert.doesNotMatch(previewTableSource, /llmReason/);
  assert.doesNotMatch(previewTableSource, /isLlmReviewed/);
  assert.match(previewTableSource, /AI 等价裁决/);

  assert.doesNotMatch(bestMatchSectionSource, /formatLlmScore/);
  assert.doesNotMatch(bestMatchSectionSource, /模型复核分/);
  assert.doesNotMatch(bestMatchSectionSource, /llmScore/);
  assert.doesNotMatch(bestMatchSectionSource, /llmReason/);
  assert.doesNotMatch(bestMatchSectionSource, /llmCommentary/);
  assert.match(bestMatchSectionSource, /AI 等价裁决|裁决结论/);

  assert.doesNotMatch(formatterSource, /formatLlmScore/);

  assert.doesNotMatch(smartFillPageSource, /llmScore\s*=/);
  assert.doesNotMatch(smartFillPageSource, /llmReason\s*=/);
  assert.doesNotMatch(smartFillPageSource, /llmCommentary\s*=/);
  assert.doesNotMatch(smartFillPageSource, /isLlmReviewed\s*=/);
});

test("智能填充页在浏览器离线时应主动收口 streaming 复核行", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(
    smartFillPageSource,
    /useEventListener\(\s*window,\s*"offline",\s*handleWindowOffline\s*\)/
  );
  assert.match(
    smartFillPageSource,
    /const handleWindowOffline = \(\) => \{[\s\S]*finalizeInterruptedLlmStreamRows\([\s\S]*stopLlmStream\(\)/
  );
});

test("smart-fill 页面不应再依赖 strict reuse，但应恢复 Word 列映射规则预填", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const helperSource = readProjectFile(
    "web/src/views/shared/word-column-mapping-rules.ts"
  );

  assert.doesNotMatch(smartFillPageSource, /StrictReuseDialog/);
  assert.doesNotMatch(smartFillPageSource, /strictReuseVisible/);
  assert.doesNotMatch(smartFillPageSource, /canStrictReusePreview/);
  assert.doesNotMatch(smartFillPageSource, /canStrictReuseExecute/);
  assert.doesNotMatch(smartFillPageSource, /canUseStrictReuse/);
  assert.doesNotMatch(smartFillPageSource, /应用到相同验规/);
  assert.match(smartFillPageSource, /word-column-mapping-rules/);
  assert.match(smartFillPageSource, /getEffectiveColumnMappingRules/);
  assert.match(helperSource, /ColumnMappingTargetField/);
  assert.match(helperSource, /ColumnMappingMatchMode/);
  assert.match(helperSource, /matchWordTableColumnsByRules/);
  assert.match(helperSource, /matchHeaderByRule/);
  assert.match(
    smartFillPageSource,
    /const buildDefaultTableConfig = \(\s*table: TableInfo,\s*selected: boolean\s*\): BatchTableConfigItem =>/
  );
  assert.match(smartFillPageSource, /matchWordTableColumnsByRules\(/);
});

test("matching API 不应再暴露 strict reuse 旧接口", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.doesNotMatch(matchingApiSource, /StrictReusePreviewRequest/);
  assert.doesNotMatch(matchingApiSource, /StrictReuseExecuteRequest/);
  assert.doesNotMatch(matchingApiSource, /strictReusePreview/);
  assert.doesNotMatch(matchingApiSource, /strictReuseExecute/);
  assert.doesNotMatch(matchingApiSource, /\/reuse\/strict\/preview/);
  assert.doesNotMatch(matchingApiSource, /\/reuse\/strict\/execute/);
});

test("配置路由与导航应恢复暴露 column-mapping-rules", () => {
  const configRouteSource = readProjectFile("web/src/router/modules/config.ts");
  const navigationManifestSource = readProjectFile(
    "shared/navigation/navigation-manifest.json"
  );

  assert.match(configRouteSource, /\/config\/column-mapping-rules/);
  assert.match(configRouteSource, /ColumnMappingRules/);
  assert.match(
    navigationManifestSource,
    /config-column-mapping-rules/
  );
  assert.match(
    navigationManifestSource,
    /page:config:column-mapping-rules/
  );
});

test("前端应恢复 column-mapping-rules API、配置页与共享 helper 文件", () => {
  assert.equal(
    existsSync(resolve(repositoryRoot, "web/src/api/column-mapping-rules.ts")),
    true
  );
  assert.equal(
    existsSync(
      resolve(repositoryRoot, "web/src/views/config/column-mapping-rules/index.vue")
    ),
    true
  );
  assert.equal(
    existsSync(
      resolve(repositoryRoot, "web/src/views/shared/word-column-mapping-rules.ts")
    ),
    true
  );
});

test("data-import 页面与映射步骤应恢复 Word 自动预填，但 Excel 仍保持手工配置", () => {
  const dataImportSource = readProjectFile("web/src/views/data-import/index.vue");
  const mappingStepSource = readProjectFile(
    "web/src/views/data-import/components/DataImportStepMapping.vue"
  );
  const helperSource = readProjectFile(
    "web/src/views/shared/word-column-mapping-rules.ts"
  );

  assert.match(dataImportSource, /column-mapping-rules/);
  assert.match(dataImportSource, /getEffectiveColumnMappingRules/);
  assert.match(dataImportSource, /applyWordRulesToWordMapping/);
  assert.match(dataImportSource, /mappingRules\.value/);
  assert.match(dataImportSource, /loadingMappingRules/);
  assert.match(dataImportSource, /loadMappingRules/);
  assert.match(dataImportSource, /applyRulesToConfig/);

  assert.match(mappingStepSource, /列映射规则/);
  assert.match(mappingStepSource, /自动预填/);
  assert.match(helperSource, /applyWordRulesToWordMapping/);
  assert.match(helperSource, /matchWordTableColumnsByRules/);
  assert.doesNotMatch(mappingStepSource, /Excel 自动预填/);
});

test("smart-fill 页面应解耦执行与下载权限，并在下载失败后保留恢复入口", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.doesNotMatch(smartFillPageSource, /const canExecuteAction = computed/);
  assert.doesNotMatch(
    smartFillPageSource,
    /const handleExecute = async \(\) => \{[\s\S]*ensurePermission\("btn:matching:download"/
  );
  assert.doesNotMatch(smartFillPageSource, /v-if="canExecuteAction"/);
  assert.match(smartFillPageSource, /v-if="canExecuteFill"/);
  assert.match(smartFillPageSource, /const handleDownloadLastResult = async \(\) => \{/);
  assert.match(smartFillPageSource, /downloadTaskResult\(taskId\.value\)/);
  assert.match(smartFillPageSource, /重新下载结果/);
  assert.match(smartFillPageSource, /v-if="taskId && canDownloadFillResult"/);
});

test("smart-fill 页面应在预览前给出 Embedding 与范围空态引导", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.match(matchConfigSource, /未检测到可用 Embedding 服务/);
  assert.match(matchConfigSource, /未检测到可用 LLM 服务/);
  assert.doesNotMatch(matchConfigSource, /本地规则拦截/);
  assert.doesNotMatch(matchConfigSource, /品牌配置/);

  assert.match(smartFillPageSource, /const previewBlockingMessage = computed\(\(\) => \{/);
  assert.match(smartFillPageSource, /请先配置可用的 Embedding 服务/);
  assert.match(smartFillPageSource, /当前范围内没有可用于匹配的验收规格/);
  assert.match(smartFillPageSource, /范围内无候选数据/);
  assert.match(smartFillPageSource, /Embedding 服务不可用/);
  assert.match(smartFillPageSource, /v-if="previewBlockingMessage"/);
});
