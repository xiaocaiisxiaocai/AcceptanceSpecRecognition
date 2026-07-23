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
  applyMatchLlmStreamEventToPreviewItem,
  applyMatchLlmStreamDisconnectToPreviewItem,
  shouldStreamMatchReview
} from "../src/views/smart-fill/components/scoreDetail.formatters.ts";
import {
  canEditMatchPreviewRow,
  canManuallyAcceptMatchPreviewBestMatch,
  canUseMatchPreviewBestMatch as canUsePreviewBestMatch
} from "../src/views/smart-fill/components/matchPreviewTable.formatters.ts";
import {
  collectEditedBackfillItems,
  reconcileMatchPreviewSelectionCache
} from "../src/views/smart-fill/components/matchPreviewTable.selection.ts";
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
  assert.doesNotMatch(decisionSummarySource, /return "核对差异后再填充";/);
});

test("llm-stream 非2xx或无 body 时应先收口中断行，且 filterEmptySourceRows helper 不应重复声明", () => {
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );
  const helperCount = (
    smartFillPageSource.match(/const getEffectiveFilterEmptySourceRows =/g) ??
    []
  ).length;

  assert.equal(helperCount, 1);
  assert.match(
    llmStreamSource,
    /if \(!response\.ok \|\| !response\.body\) \{[\s\S]*finalizeInterruptedLlmStreamRows\([\s\S]*llmStreamController\.value === controller/
  );
});

test("AI 判定不同或不确定时不应允许前端确认采用", () => {
  const different = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      llmEquivalence: {
        verdict: "different",
        reasonType: "semantic_difference",
        confidence: 0.9
      }
    }
  });
  const uncertain = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      llmEquivalence: {
        verdict: "uncertain",
        reasonType: "uncertain",
        confidence: 0.4
      }
    }
  });

  assert.equal(canUsePreviewBestMatch(different, "manual"), false);
  assert.equal(canUsePreviewBestMatch(uncertain, "manual"), false);
});

test("需要确认和不建议填充行应支持人工编辑，需要确认行可人工确认采用", () => {
  const review = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      llmEquivalence: {
        verdict: "different",
        reasonType: "semantic_difference",
        confidence: 0.9
      }
    }
  });
  const blocked = createPreviewItem({
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "reject"
    }
  });
  const dataTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewDataTable.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );

  assert.equal(canUsePreviewBestMatch(review, "manual"), false);
  assert.equal(canManuallyAcceptMatchPreviewBestMatch(review, "manual"), true);
  assert.equal(canEditMatchPreviewRow(review, "manual"), true);
  assert.equal(canEditMatchPreviewRow(blocked, "blocked"), true);
  assert.match(
    previewTableSource,
    /:can-manually-accept-best-match="canManuallyAcceptBestMatch"/
  );
  assert.match(dataTableSource, /canShowClearSelection/);
});

test("llm-stream 收口只应处理本次仍待完成的复核行", () => {
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );

  assert.match(llmStreamSource, /activeLlmStreamPendingRowKeys/);
  assert.match(
    llmStreamSource,
    /event === "review\.done" \|\| event === "review\.error"[\s\S]*activeLlmStreamPendingRowKeys\.value\.delete/
  );
  assert.match(
    llmStreamSource,
    /!pendingRowKeys\.has\(\s*buildLlmStreamRowKey\(tableResult\.tableIndex, item\.rowIndex\)\s*\)/
  );
});

test("主动停止 llm-stream 时应先收口 pending 行再清理状态", () => {
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );
  const stopBlock =
    llmStreamSource.match(
      /const stopLlmStream = \(\) => \{[\s\S]*?\n  \};/
    )?.[0] ?? "";

  assert.match(stopBlock, /finalizeInterruptedLlmStreamRows\(/);
  assert.ok(
    stopBlock.indexOf("finalizeInterruptedLlmStreamRows(") <
      stopBlock.indexOf("activeLlmStreamPendingRowKeys.value.clear()")
  );
});

test("stream.complete 应收口服务端未逐行返回 review 事件的 pending 行", () => {
  const item = createPreviewItem({
    llmReviewStage: undefined,
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      isAmbiguous: true
    }
  });

  applyMatchLlmStreamEventToPreviewItem(item, "stream.complete", {
    tableIndex: 0,
    rowIndex: 0,
    completedRowKeys: ["0:0"]
  } as never);

  assert.equal(item.llmReviewStage, "done");
  assert.equal(item.llmReviewError, undefined);
});

test("review.done 事件携带权威最佳匹配时应替换前端旧候选", () => {
  const item = createPreviewItem({
    llmReviewStage: "streaming",
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 1001,
      acceptance: "旧验收",
      remark: "旧备注"
    }
  });

  applyMatchLlmStreamEventToPreviewItem(item, "review.done", {
    rowIndex: item.rowIndex,
    decision: "autoApply",
    score: 0.91,
    reason: "AI 判定可直接采用",
    commentary: "服务端权威重算后采用新候选",
    reviewApprovalToken: "token-for-2002",
    bestMatch: {
      ...item.bestMatch!,
      specId: 2002,
      project: "项目B",
      specification: "200V",
      acceptance: "新验收",
      remark: "新备注",
      decision: "autoApply",
      reviewApprovalToken: "token-for-2002"
    }
  });

  assert.equal(item.bestMatch?.specId, 2002);
  assert.equal(item.bestMatch?.project, "项目B");
  assert.equal(item.bestMatch?.specification, "200V");
  assert.equal(item.bestMatch?.acceptance, "新验收");
  assert.equal(item.bestMatch?.remark, "新备注");
  assert.equal(item.bestMatch?.reviewApprovalToken, "token-for-2002");
});

test("stream.complete 应把已开始但未收到终态的 streaming 行转人工确认", () => {
  const item = createPreviewItem({
    llmReviewStage: "streaming",
    llmReviewDraft: "正在复核",
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      decision: "manualReview",
      isAmbiguous: true
    }
  });

  applyMatchLlmStreamEventToPreviewItem(item, "stream.complete", {
    tableIndex: 0,
    rowIndex: 0,
    completedRowKeys: ["0:0"]
  } as never);

  assert.equal(item.bestMatch?.decision, "manualReview");
  assert.equal(item.llmReviewStage, "error");
  assert.equal(item.llmReviewDraft, "");
  assert.equal(item.llmReviewError, "LLM复核未返回终态，已转为人工确认");
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

const getLlmStreamPayloadBlock = (llmStreamSource: string) => {
  const match = llmStreamSource.match(
    /const buildPayload =[\s\S]*?buildLlmStreamPayload[\s\S]*?createMatchLlmStreamRequest\(\{[\s\S]*?\}\)\);[\s\S]*?const payload = buildPayload\(scope, llmItems, matchConfig\.value\)/
  );
  assert.ok(
    match,
    "应能定位 startLlmStream 中发送 llm-stream 的 payload 代码块"
  );
  return match[0];
};

test("批量预览 Tab 应维护本地可切换状态，而不是把只读 computed 绑定给 v-model", () => {
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );

  assert.match(
    previewTabsSource,
    /import\s+\{\s*computed,\s*ref,\s*watch\s*\}\s+from "vue";/
  );
  assert.match(previewTabsSource, /const activeTab = ref\(/);
  assert.match(
    previewTabsSource,
    /watch\(\s*\(\)\s*=>\s*props\.results,\s*results =>[\s\S]*activeTab\.value/
  );
  assert.doesNotMatch(previewTabsSource, /const activeTab = computed\(/);
});

test("匹配配置文案应说明高置信阈值会影响确定性自动通过", () => {
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.doesNotMatch(matchConfigSource, /默认选中/);
  assert.doesNotMatch(matchConfigSource, /自动勾选仍以高置信阈值为准/);
  assert.match(matchConfigSource, /高置信阈值会参与确定性自动通过/);
  assert.doesNotMatch(matchConfigSource, /高置信阈值只用于结果分层展示/);
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

  const bestMatchCellSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewBestMatchCell.vue"
  );

  assert.match(
    bestMatchCellSource,
    /!shouldHideInlineLlmEquivalenceSummary\(\s*llmEquivalence\.value,\s*props\.item\.bestMatch\?\.score\s*\)/
  );
  assert.equal(
    (
      bestMatchCellSource.match(
        /!shouldHideInlineLlmEquivalenceSummary\(\s*llmEquivalence\.value,\s*props\.item\.bestMatch\?\.score\s*\)/g
      ) ?? []
    ).length,
    1
  );
});

test("说明列应只保留异常原因，不再重复展示最佳匹配中的证据与 AI 摘要", () => {
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const dataTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewDataTable.vue"
  );
  const reasonCellSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewReasonCell.vue"
  );
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.formatters.ts"
  );
  const reasonColumnMatch = dataTableSource.match(
    /<!-- 不匹配原因 \/ 复核说明 -->[\s\S]*?<!-- 操作 -->/
  );
  const reasonPredicateMatch = formatterSource.match(
    /export const shouldShowReasonColumnForItem[\s\S]*?!!item\.llmReviewError;/
  );

  assert.ok(reasonColumnMatch, "应能定位说明列代码块");
  assert.ok(reasonPredicateMatch, "应能定位说明列显示条件");
  const reasonColumnBlock = reasonColumnMatch[0];
  const reasonPredicateBlock = reasonPredicateMatch[0];

  assert.match(previewTableSource, /const hasReasonColumn = computed\(/);
  assert.match(reasonPredicateBlock, /!!item\.noMatchReason/);
  assert.match(
    reasonPredicateBlock,
    /!!item\.bestMatch\?\.conflictSummary\?\.length/
  );
  assert.match(reasonPredicateBlock, /!!item\.llmReviewError/);
  assert.doesNotMatch(
    reasonPredicateBlock,
    /!!item\.bestMatch\?\.issues\?\.length/
  );
  assert.doesNotMatch(
    reasonPredicateBlock,
    /!!item\.bestMatch\?\.evidenceSummary\?\.length/
  );
  assert.doesNotMatch(
    reasonPredicateBlock,
    /!!item\.bestMatch\?\.llmEquivalence/
  );

  assert.doesNotMatch(reasonCellSource, /问题：/);
  assert.doesNotMatch(reasonCellSource, /建议：/);
  assert.doesNotMatch(reasonCellSource, /证据：/);
  assert.doesNotMatch(reasonCellSource, /AI 裁决：/);
  assert.match(reasonColumnBlock, /label="异常\/原因"/);
  assert.doesNotMatch(reasonColumnBlock, /label="说明"/);
  assert.match(reasonCellSource, /复核异常：/);
  assert.match(reasonCellSource, /冲突：/);
});

test("顶部筛选应区分 100%可填充 与低于100%的可填充，行内仍统一显示可直接填充", () => {
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.formatters.ts"
  );
  const statsBarSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewStatsBar.vue"
  );

  assert.match(formatterSource, /export const isExactFillable = \(/);
  assert.match(formatterSource, /export const isPartialFillable = \(/);
  assert.match(
    formatterSource,
    /item\.bestMatch\?\.selectionMode === "exactShortcut"/
  );
  assert.match(
    formatterSource,
    /item\.bestMatch\.selectionMode !== "exactShortcut"/
  );
  assert.doesNotMatch(formatterSource, /item\.bestMatch\?\.score === 1/);
  assert.match(formatterSource, /return "可直接填充";/);
  assert.match(
    statsBarSource,
    /100%精确直达 \(\{\{ stats\.exactFillable \}\}\)/
  );
  assert.match(
    statsBarSource,
    /AI\/普通可填充 \(\{\{ stats\.partialFillable \}\}\)/
  );
  assert.doesNotMatch(statsBarSource, /可填充 \(\{\{ stats\.fillable \}\}\)/);
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
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.formatters.ts"
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
  assert.doesNotMatch(
    smartFillPageSource,
    /llmReviewScore:\s*s\.llmReviewScore/
  );
  assert.doesNotMatch(smartFillPageSource, /llmReviewScore\?: number;/);
  assert.doesNotMatch(smartFillPageSource, /decision:\s*s\.decision/);
  assert.match(
    `${previewTableSource}\n${formatterSource}`,
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
  const previewTableTypesSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.types.ts"
  );
  const selectionSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.selection.ts"
  );
  const executionHelperSource = readProjectFile(
    "web/src/views/smart-fill/smartFillExecution.helpers.ts"
  );
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(fillMappingBlock, /reviewApprovalToken\?: string;/);
  assert.match(previewTabsSource, /reviewApprovalToken\?: string;/);
  assert.match(previewTableTypesSource, /reviewApprovalToken\?: string;/);
  assert.match(
    `${previewTableSource}\n${selectionSource}`,
    /reviewApprovalToken:\s*item\.bestMatch\?\.reviewApprovalToken/
  );
  assert.match(
    executionHelperSource,
    /reviewApprovalToken:\s*s\.reviewApprovalToken/
  );
});

test("智能填充执行请求应透传本次导出覆盖值，而不是只发送规格ID", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const previewTableTypesSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.types.ts"
  );
  const selectionSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.selection.ts"
  );
  const executionHelperSource = readProjectFile(
    "web/src/views/smart-fill/smartFillExecution.helpers.ts"
  );
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(fillMappingBlock, /overrideAcceptance\?: string;/);
  assert.match(fillMappingBlock, /overrideRemark\?: string;/);
  assert.match(previewTabsSource, /overrideAcceptance\?: string;/);
  assert.match(previewTabsSource, /overrideRemark\?: string;/);
  assert.match(previewTableTypesSource, /overrideAcceptance\?: string;/);
  assert.match(previewTableTypesSource, /overrideRemark\?: string;/);
  assert.match(
    `${previewTableSource}\n${selectionSource}`,
    /overrideAcceptance:\s*.*overrideAcceptance/
  );
  assert.match(
    `${previewTableSource}\n${selectionSource}`,
    /overrideRemark:\s*.*overrideRemark/
  );
  assert.match(
    executionHelperSource,
    /overrideAcceptance:\s*s\.overrideAcceptance/
  );
  assert.match(executionHelperSource, /overrideRemark:\s*s\.overrideRemark/);
});

test("智能填充前端应支持仅精确匹配模式与未命中行手工填充", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );
  const previewTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTable.vue"
  );
  const previewTableTypesSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.types.ts"
  );
  const textCellSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTextCell.vue"
  );
  const selectionSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.selection.ts"
  );
  const previewRequestSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewRequest.ts"
  );
  const executionHelperSource = readProjectFile(
    "web/src/views/smart-fill/smartFillExecution.helpers.ts"
  );
  const matchConfigBlock = getInterfaceBlock(matchingApiSource, "MatchConfig");
  const fillMappingBlock = getInterfaceBlock(matchingApiSource, "FillMapping");

  assert.match(matchConfigBlock, /exactMatchOnly\?: boolean;/);
  assert.match(matchingApiSource, /exactMatchOnly:\s*false/);
  assert.match(fillMappingBlock, /manualFill\?: boolean;/);
  assert.match(matchConfigSource, /仅精确匹配/);
  assert.match(matchConfigSource, /项目\+规格完全一致/);
  assert.match(previewTabsSource, /manualFill\?: boolean;/);
  assert.match(previewTableTypesSource, /manualFill\?: boolean;/);
  assert.doesNotMatch(
    previewTableSource,
    /const openEditDialog = \(item: MatchPreviewItem\) => \{\s*if \(!item\.bestMatch/
  );
  assert.doesNotMatch(
    previewTableSource,
    /const handleSaveEditedSelection = \(\) => \{[\s\S]*?if \(!item\?\.bestMatch/
  );
  assert.match(previewTableSource, /type: "manual"/);
  assert.match(selectionSource, /manualFill:\s*selection\?\.type === "manual"/);
  assert.match(textCellSource, /已手工填写/);
  assert.match(executionHelperSource, /manualFill:\s*s\.manualFill/);
  assert.match(
    previewRequestSource,
    /if \(matchConfig\.value\.exactMatchOnly\) \{[\s\S]*return;[\s\S]*\}[\s\S]*startLlmStream\(\)/
  );
});

test("智能填充预览页应提供编辑弹窗、保存并采用和已编辑标记", () => {
  const dataTableSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewDataTable.vue"
  );
  const editDialogSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewEditDialog.vue"
  );
  const textCellSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewTextCell.vue"
  );

  assert.match(dataTableSource, />\s*编辑\s*<\/el-button>/);
  assert.match(editDialogSource, /保存并采用/);
  assert.match(editDialogSource, /仅本次导出使用/);
  assert.match(editDialogSource, /完整替换原值，不会与旧值叠加/);
  assert.match(editDialogSource, /target\.select\(\)/);
  assert.equal(
    (editDialogSource.match(/@focus="selectExistingValue"/g) ?? []).length,
    2
  );
  assert.match(textCellSource, /已编辑/);
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
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/matchPreviewTable.formatters.ts"
  );
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );
  const llmStreamPayloadBlock = getLlmStreamPayloadBlock(llmStreamSource);
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.match(
    formatterSource,
    /export const isMatchPreviewAutoApply = \(item: MatchPreviewItem\) =>[\s\S]*getMatchPreviewDecision\(item\) === "autoApply";/
  );
  assert.match(
    formatterSource,
    /export const isHighConfidenceMatchPreview = \(item: MatchPreviewItem\) =>[\s\S]*isMatchPreviewAutoApply\(item\) && item\.confidenceLevel === "high"/
  );
  assert.doesNotMatch(
    formatterSource,
    /export const isHighConfidenceMatchPreview = \(item: MatchPreviewItem\) =>[\s\S]*llmEquivalence[\s\S]*export const canUseMatchPreviewBestMatch/
  );
  assert.doesNotMatch(
    llmStreamSource,
    /const shouldStreamReview = \(item: MatchPreviewItem\) =>[\s\S]*?llmEquivalence/
  );
  assert.doesNotMatch(
    `${smartFillPageSource}\n${llmStreamSource}`,
    /authorizedFetch\(/
  );
  assert.match(matchingApiSource, /export interface MatchLlmStreamRequest \{/);
  assert.match(matchingApiSource, /export type MatchLlmStreamEvent =/);
  assert.match(
    matchingApiSource,
    /export const createMatchLlmStreamRequest = \(/
  );
  assert.match(
    matchingApiSource,
    /export const requestMatchLlmStream = (async )?\(/
  );
  assert.match(
    `${smartFillPageSource}\n${llmStreamSource}`,
    /requestMatchLlmStream,\s*createMatchLlmStreamRequest|createMatchLlmStreamRequest,[\s\S]*requestMatchLlmStream/
  );
  assert.doesNotMatch(matchingApiSource, /MatchingStrategy/);
  assert.doesNotMatch(matchingApiSource, /matchingStrategy/);
  assert.match(
    llmStreamPayloadBlock,
    /createMatchLlmStreamRequest\(\{[\s\S]*customerId:\s*s\.customerId,[\s\S]*processId:\s*s\.processId,[\s\S]*machineModelId:\s*s\.machineModelId,[\s\S]*config[\s\S]*const payload = buildPayload\(scope, llmItems, matchConfig\.value\)/
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
  assert.match(
    previewTableSource,
    /const syncSelectionsWithItems = \(\) => \{/
  );
  assert.match(
    previewTableSource,
    /watch\(\s*selectionSyncKey,\s*\(\)\s*=>\s*syncSelectionsWithItems\(\),\s*\{\s*immediate:\s*true\s*\}\s*\)/
  );
});

test("未激活表格的选择缓存应在 LLM 复核签发 token 后同步为可执行选择", () => {
  const reviewedItem = createPreviewItem({
    rowIndex: 3,
    llmReviewStage: "done",
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 2002,
      decision: "autoApply",
      reviewApprovalToken: "token-tab-2"
    }
  });
  const selectionCache = new Map([
    [
      1,
      [
        {
          rowIndex: 3,
          selected: false,
          specId: 2002
        }
      ]
    ]
  ]);

  reconcileMatchPreviewSelectionCache(
    [
      {
        tableIndex: 1,
        totalMatched: 1,
        highConfidenceCount: 1,
        mediumConfidenceCount: 0,
        lowConfidenceCount: 0,
        ambiguousCount: 0,
        items: [reviewedItem]
      }
    ],
    selectionCache
  );

  assert.deepEqual(selectionCache.get(1), [
    {
      rowIndex: 3,
      selected: true,
      specId: 2002,
      manualConfirmed: false,
      manualFill: false,
      reviewApprovalToken: "token-tab-2",
      overrideAcceptance: undefined,
      overrideRemark: undefined
    }
  ]);
});

test("用户手动取消的未激活表格行不应在 LLM 签发 token 后被重新选中", () => {
  const reviewedItem = createPreviewItem({
    rowIndex: 3,
    llmReviewStage: "done",
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 2002,
      decision: "autoApply",
      reviewApprovalToken: "token-tab-2"
    }
  });
  const selectionCache = new Map([
    [
      1,
      [
        {
          rowIndex: 3,
          selected: false,
          specId: 2002,
          manualCleared: true
        }
      ]
    ]
  ]);

  reconcileMatchPreviewSelectionCache(
    [
      {
        tableIndex: 1,
        totalMatched: 1,
        highConfidenceCount: 1,
        mediumConfidenceCount: 0,
        lowConfidenceCount: 0,
        ambiguousCount: 0,
        items: [reviewedItem]
      }
    ],
    selectionCache
  );

  assert.deepEqual(selectionCache.get(1), [
    {
      rowIndex: 3,
      selected: false,
      specId: 2002,
      manualConfirmed: undefined,
      manualFill: false,
      reviewApprovalToken: undefined,
      manualCleared: true,
      overrideAcceptance: undefined,
      overrideRemark: undefined
    }
  ]);
});

test("用户手动取消的未激活表格行在 LLM 复核中或暂不可用时应保留取消标记", () => {
  const streamingItem = createPreviewItem({
    rowIndex: 3,
    llmReviewStage: "streaming",
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 2002,
      decision: "manualReview"
    }
  });
  const blockedItem = createPreviewItem({
    rowIndex: 4,
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 2003,
      decision: "reject"
    }
  });
  const selectionCache = new Map([
    [
      1,
      [
        {
          rowIndex: 3,
          selected: false,
          specId: 2002,
          manualCleared: true
        },
        {
          rowIndex: 4,
          selected: false,
          specId: 2003,
          manualCleared: true
        }
      ]
    ]
  ]);

  reconcileMatchPreviewSelectionCache(
    [
      {
        tableIndex: 1,
        totalMatched: 2,
        highConfidenceCount: 0,
        mediumConfidenceCount: 2,
        lowConfidenceCount: 0,
        ambiguousCount: 0,
        items: [streamingItem, blockedItem]
      }
    ],
    selectionCache
  );

  assert.deepEqual(selectionCache.get(1), [
    {
      rowIndex: 3,
      selected: false,
      specId: 2002,
      manualConfirmed: undefined,
      manualFill: false,
      reviewApprovalToken: undefined,
      manualCleared: true,
      overrideAcceptance: undefined,
      overrideRemark: undefined
    },
    {
      rowIndex: 4,
      selected: false,
      specId: 2003,
      manualConfirmed: undefined,
      manualFill: false,
      reviewApprovalToken: undefined,
      manualCleared: true,
      overrideAcceptance: undefined,
      overrideRemark: undefined
    }
  ]);
});

test("用户手动取消的编辑行不应进入规格回填候选", () => {
  const item = createPreviewItem({ rowIndex: 4 });
  const selectedSpecs = new Map([[4, null]]);
  const editedOverrides = new Map([
    [
      4,
      {
        overrideAcceptance: "人工改写验收",
        overrideRemark: "人工改写备注"
      }
    ]
  ]);
  const manualClearedRows = new Set([4]);

  assert.deepEqual(
    collectEditedBackfillItems(
      [item],
      editedOverrides,
      selectedSpecs,
      manualClearedRows
    ),
    []
  );
});

test("未命中行空白手工填写不应进入规格回填候选，已有规格清空字段仍应保留", () => {
  const manualItem = createPreviewItem({
    rowIndex: 5,
    bestMatch: undefined,
    hasMatch: false,
    confidenceLevel: "none"
  });
  const matchedItem = createPreviewItem({
    rowIndex: 6,
    bestMatch: {
      ...createPreviewItem().bestMatch!,
      specId: 2006
    }
  });
  const selectedSpecs = new Map([
    [5, { type: "manual" as const, manualConfirmed: true }],
    [6, { type: "best" as const, manualConfirmed: true }]
  ]);
  const editedOverrides = new Map([
    [5, { overrideAcceptance: "", overrideRemark: "" }],
    [6, { overrideAcceptance: "", overrideRemark: "" }]
  ]);

  assert.deepEqual(
    collectEditedBackfillItems(
      [manualItem, matchedItem],
      editedOverrides,
      selectedSpecs
    ),
    [
      {
        rowIndex: 6,
        specId: 2006,
        sourceProject: "项目A",
        sourceSpecification: "100V",
        originalAcceptance: "通过",
        originalRemark: "备注",
        overrideAcceptance: "",
        overrideRemark: "",
        actionType: "update"
      }
    ]
  );
});

test("SSE 事件缺少 tableIndex 时应直接丢弃，不能再跨表按 rowIndex 回退匹配", () => {
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );

  assert.match(
    llmStreamSource,
    /if \(rowData\.tableIndex === undefined \|\| rowData\.tableIndex === null\) \{\s*return;\s*\}/
  );
  assert.match(
    llmStreamSource,
    /const tableResult = batchPreviewResults\.value\.find\(\s*tableResult => tableResult\.tableIndex === rowData\.tableIndex\s*\)/
  );
});

test("批量链路应让表级 filterEmptySourceRows 回退到全局配置", () => {
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const previewRequestSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewRequest.ts"
  );
  const executionHelperSource = readProjectFile(
    "web/src/views/smart-fill/smartFillExecution.helpers.ts"
  );

  assert.match(
    smartFillPageSource,
    /const getEffectiveFilterEmptySourceRows = \(\s*tableConfig:\s*\{[\s\S]*?filterEmptySourceRows\?: boolean;[\s\S]*?\}\s*\) =>[\s\S]*tableConfig\.filterEmptySourceRows \?\?[\s\S]*matchConfig\.value\.filterEmptySourceRows \?\?[\s\S]*true/
  );
  assert.match(
    previewRequestSource,
    /filterEmptySourceRows:\s*getEffectiveFilterEmptySourceRows\(t\)/
  );
  assert.match(
    executionHelperSource,
    /filterEmptySourceRows:\s*resolveFilterEmptySourceRows\(config\)/
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
  const bestMatchCellSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchPreviewBestMatchCell.vue"
  );
  const bestMatchSectionSource = readProjectFile(
    "web/src/views/smart-fill/components/ScoreDetailBestMatchSection.vue"
  );
  const formatterSource = readProjectFile(
    "web/src/views/smart-fill/components/scoreDetail.formatters.ts"
  );
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );

  assert.doesNotMatch(matchingApiSource, /\bllmScore\??:/);
  assert.doesNotMatch(matchingApiSource, /\bllmReason\??:/);
  assert.doesNotMatch(matchingApiSource, /\bllmCommentary\??:/);
  assert.doesNotMatch(matchingApiSource, /\bisLlmReviewed\??:/);

  assert.doesNotMatch(previewTableSource, /LLM_REVIEW_PASS_THRESHOLD/);
  assert.doesNotMatch(previewTableSource, /llmScore/);
  assert.doesNotMatch(previewTableSource, /llmReason/);
  assert.doesNotMatch(previewTableSource, /isLlmReviewed/);
  assert.match(`${previewTableSource}\n${bestMatchCellSource}`, /AI 等价裁决/);

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
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const llmStreamSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillLlmStream.ts"
  );

  assert.match(
    smartFillPageSource,
    /useEventListener\(\s*window,\s*"offline",\s*handleWindowOffline\s*\)/
  );
  assert.match(
    llmStreamSource,
    /const handleWindowOffline = \(\) => \{[\s\S]*finalizeInterruptedLlmStreamRows\([\s\S]*stopLlmStream\(\)/
  );
});

test("smart-fill 页面不应再依赖 strict reuse，但应恢复 Word 列映射规则预填", () => {
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const uploadedTablesSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillUploadedTables.ts"
  );
  const helperSource = readProjectFile(
    "web/src/views/shared/word-column-mapping-rules.ts"
  );

  assert.doesNotMatch(smartFillPageSource, /StrictReuseDialog/);
  assert.doesNotMatch(smartFillPageSource, /strictReuseVisible/);
  assert.doesNotMatch(smartFillPageSource, /canStrictReusePreview/);
  assert.doesNotMatch(smartFillPageSource, /canStrictReuseExecute/);
  assert.doesNotMatch(smartFillPageSource, /canUseStrictReuse/);
  assert.doesNotMatch(smartFillPageSource, /应用到相同验规/);
  assert.match(uploadedTablesSource, /word-column-mapping-rules/);
  assert.match(uploadedTablesSource, /getEffectiveColumnMappingRules/);
  assert.match(helperSource, /ColumnMappingTargetField/);
  assert.match(helperSource, /ColumnMappingMatchMode/);
  assert.match(helperSource, /matchWordTableColumnsByRules/);
  assert.match(helperSource, /matchHeaderByRule/);
  assert.match(
    uploadedTablesSource,
    /const buildDefaultTableConfig = \(\s*table: TableInfo,\s*selected: boolean\s*\): BatchTableConfigItem =>/
  );
  assert.match(uploadedTablesSource, /matchWordTableColumnsByRules\(/);
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
  assert.match(navigationManifestSource, /config-column-mapping-rules/);
  assert.match(navigationManifestSource, /page:config:column-mapping-rules/);
});

test("前端应恢复 column-mapping-rules API、配置页与共享 helper 文件", () => {
  assert.equal(
    existsSync(resolve(repositoryRoot, "web/src/api/column-mapping-rules.ts")),
    true
  );
  assert.equal(
    existsSync(
      resolve(
        repositoryRoot,
        "web/src/views/config/column-mapping-rules/index.vue"
      )
    ),
    true
  );
  assert.equal(
    existsSync(
      resolve(
        repositoryRoot,
        "web/src/views/shared/word-column-mapping-rules.ts"
      )
    ),
    true
  );
});

test("data-import 页面与映射步骤应恢复 Word 自动预填，但 Excel 仍保持手工配置", () => {
  const dataImportSource = readProjectFile(
    "web/src/views/data-import/index.vue"
  );
  const dataImportPageSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportPage.ts"
  );
  const mappingStepSource = readProjectFile(
    "web/src/views/data-import/components/DataImportStepMapping.vue"
  );
  const helperSource = readProjectFile(
    "web/src/views/shared/word-column-mapping-rules.ts"
  );

  assert.match(dataImportPageSource, /column-mapping-rules/);
  assert.match(dataImportPageSource, /getEffectiveColumnMappingRules/);
  assert.match(dataImportPageSource, /applyWordRulesToWordMapping/);
  assert.match(dataImportPageSource, /mappingRules\.value/);
  assert.match(
    `${dataImportSource}\n${dataImportPageSource}`,
    /loadingMappingRules/
  );
  assert.match(
    `${dataImportSource}\n${dataImportPageSource}`,
    /loadMappingRules/
  );
  assert.match(dataImportPageSource, /applyRulesToConfig/);

  assert.match(mappingStepSource, /列映射规则/);
  assert.match(mappingStepSource, /自动预填/);
  assert.match(helperSource, /applyWordRulesToWordMapping/);
  assert.match(helperSource, /matchWordTableColumnsByRules/);
  assert.doesNotMatch(mappingStepSource, /Excel 自动预填/);
});

test("smart-fill 页面应解耦执行与下载权限，并在下载失败后保留恢复入口", () => {
  const smartFillPageSource = readProjectFile(
    "web/src/views/smart-fill/index.vue"
  );
  const executionSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillExecution.ts"
  );
  const previewStepSource = readProjectFile(
    "web/src/views/smart-fill/components/SmartFillPreviewStep.vue"
  );

  assert.doesNotMatch(
    `${smartFillPageSource}\n${executionSource}\n${previewStepSource}`,
    /const canExecuteAction = computed/
  );
  assert.doesNotMatch(
    executionSource,
    /const handleExecute = async \(\) => \{[\s\S]*ensurePermission\("btn:matching:download"/
  );
  assert.doesNotMatch(previewStepSource, /v-if="canExecuteAction"/);
  assert.match(previewStepSource, /v-if="!taskId && canExecuteFill"/);
  assert.match(
    executionSource,
    /const handleDownloadLastResult = async \(\) => \{/
  );
  assert.match(executionSource, /downloadTaskResult\(taskId\.value\)/);
  assert.match(previewStepSource, /重新下载/);
  assert.match(previewStepSource, /v-if="taskId && canDownloadFillResult"/);
});

test("smart-fill 页面应在预览前给出 Embedding 与范围空态引导", () => {
  const previewBlockingSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewBlocking.ts"
  );
  const previewStepSource = readProjectFile(
    "web/src/views/smart-fill/components/SmartFillPreviewStep.vue"
  );
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.match(matchConfigSource, /embeddingStatusText/);
  assert.match(matchConfigSource, /llmStatusText/);
  assert.doesNotMatch(matchConfigSource, /本地规则拦截/);
  assert.doesNotMatch(matchConfigSource, /品牌配置/);

  assert.match(
    previewBlockingSource,
    /const previewBlockingMessage = computed\(\(\) => \{/
  );
  assert.match(previewBlockingSource, /请先配置可用的 Embedding 服务/);
  assert.match(previewBlockingSource, /当前范围内没有可用于匹配的验收规格/);
  assert.match(previewBlockingSource, /范围内无候选数据/);
  assert.match(previewBlockingSource, /Embedding 服务不可用/);
  assert.match(
    previewStepSource,
    /<template v-else>[\s\S]*v-if="previewBlockingMessage"/
  );
});

test("智能填充应按运行可用性自动选择 AI 服务", () => {
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.match(matchConfigSource, /loadRuntimeAiSelectionsSettled/);
  assert.match(matchConfigSource, /\["embedding", "llm"\]/);
  assert.match(matchConfigSource, /applyMatchConfigRuntimeAiSelections/);
  assert.match(matchConfigSource, /createAiSelectionRetryController/);
  assert.match(matchConfigSource, /onActivated/);
  assert.doesNotMatch(matchConfigSource, /getAiServiceList/);
  assert.doesNotMatch(
    matchConfigSource,
    /v-model="config\.(?:embedding|llm)ServiceId"/
  );
});

test("导入数据页应按运行可用性自动选择 AI 服务", () => {
  const dataImportTargetSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportTarget.ts"
  );

  assert.match(dataImportTargetSource, /loadRuntimeAiSelectionsSettled/);
  assert.match(dataImportTargetSource, /\["embedding", "llm"\]/);
  assert.match(dataImportTargetSource, /applyDataImportRuntimeAiSelections/);
  assert.match(dataImportTargetSource, /createAiSelectionRetryController/);
  assert.match(dataImportTargetSource, /onDeactivated/);
  assert.doesNotMatch(dataImportTargetSource, /getAiServiceList/);
});

test("仅精确匹配模式不应被 Embedding 空态阻塞，并应给出明显入口", () => {
  const previewBlockingSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewBlocking.ts"
  );
  const matchConfigSource = readProjectFile(
    "web/src/views/smart-fill/components/MatchConfig.vue"
  );

  assert.match(
    previewBlockingSource,
    /const getPrePreviewBlockingMessage = \(\) => \{[\s\S]*if \(toValue\(matchConfig\)\.exactMatchOnly\) \{[\s\S]*return "";/,
    "仅精确匹配开启后，前端不应再要求可用 Embedding 服务"
  );
  assert.match(matchConfigSource, /仅匹配项目\+规格完全一致/);
  assert.match(
    matchConfigSource,
    /无需 AI\/Embedding|无需配置可用 Embedding|无需 AI\/Embedding。|即使未配置可用 Embedding/
  );
  assert.match(matchConfigSource, /即使未配置可用 Embedding/);
});

test("预览零命中但存在源行时应展示未命中行，不能直接进入空态", () => {
  const previewRequestSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewRequest.ts"
  );

  assert.match(
    previewRequestSource,
    /const hasPreviewRows = res\.data\.tables\.some\(\s*table => table\.items\.length > 0\s*\);/
  );
  assert.match(previewRequestSource, /if \(!hasPreviewRows\) \{/);
  assert.doesNotMatch(
    previewRequestSource,
    /if \(res\.data\.totalMatched === 0\) \{[\s\S]*previewState\.value = "emptyResults"/
  );
});
