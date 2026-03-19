<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  type MatchCandidateOption,
  type MatchPreviewItem,
  MatchingStrategy
} from "@/api/matching";

const props = defineProps<{
  visible: boolean;
  item: MatchPreviewItem | null;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});

const topCandidates = computed(() => props.item?.bestMatch?.topCandidates ?? []);
const comparisonRank = ref<number | null>(null);
const diffViewMode = ref<"field" | "raw">("raw");
const rawOnlyDiff = ref(true);
const inlineDiffCache = new Map<
  string,
  { leftHtml: string; rightHtml: string; isSame: boolean }
>();

const formatScore = (score: number) => `${(score * 100).toFixed(1)}%`;

const formatLlmScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return `${score.toFixed(1)}分`;
};

const formatOptionalScore = (score?: number) => {
  if (score === undefined || score === null) return "-";
  return formatScore(score);
};

const getStrategyText = (strategy?: MatchingStrategy) => {
  return strategy === MatchingStrategy.MultiStage ? "多阶段重排" : "基础单阶段";
};

const getConfidenceClass = (
  level: string
): "success" | "warning" | "danger" | "info" => {
  const map: Record<string, "success" | "warning" | "danger" | "info"> = {
    high: "success",
    medium: "warning",
    low: "danger"
  };
  return map[level] || "info";
};

const getConfidenceText = (level: string) => {
  const map: Record<string, string> = {
    high: "高",
    medium: "中",
    low: "低"
  };
  return map[level] || "无";
};

const getCandidateDelta = (candidate: MatchCandidateOption) => {
  const first = topCandidates.value[0];
  if (!first || candidate.rank === 1) return "最佳候选";
  return `较 Top1 低 ${(first.score - candidate.score) * 100 >= 0 ? ((first.score - candidate.score) * 100).toFixed(1) : "0.0"} 分`;
};

const getSortedScoreDetails = (candidate: MatchCandidateOption) => {
  return Object.entries(candidate.scoreDetails ?? {}).sort(
    ([leftKey], [rightKey]) => {
      const order = [
        "Final",
        "Embedding",
        "ProjectMatch",
        "SpecificationText",
        "NumberUnit",
        "KeywordOverlap",
        "ConflictPenalty"
      ];
      const leftIndex = order.indexOf(leftKey);
      const rightIndex = order.indexOf(rightKey);
      if (leftIndex === -1 && rightIndex === -1) {
        return leftKey.localeCompare(rightKey);
      }
      if (leftIndex === -1) return 1;
      if (rightIndex === -1) return -1;
      return leftIndex - rightIndex;
    }
  );
};

const getScoreLabel = (key: string) => {
  const map: Record<string, string> = {
    Final: "最终",
    Embedding: "Embedding",
    ProjectMatch: "项目",
    SpecificationText: "规格文本",
    NumberUnit: "数值单位",
    KeywordOverlap: "关键词",
    ConflictPenalty: "冲突惩罚"
  };
  return map[key] || key;
};

const escapeHtml = (text: string) =>
  text
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");

const formatHtmlFragment = (text: string) =>
  escapeHtml(text).replaceAll("\n", "<br />");

const renderHtmlText = (text?: string) => {
  if (!text || text.length === 0) {
    return `<span class="placeholder-text">（空）</span>`;
  }
  return formatHtmlFragment(text);
};

const buildInlineDiffHtml = (leftText?: string, rightText?: string) => {
  const oldText = leftText ?? "";
  const newText = rightText ?? "";
  const cacheKey = `${oldText}\u0000${newText}`;
  const cached = inlineDiffCache.get(cacheKey);
  if (cached) return cached;

  if (oldText === newText) {
    const same = {
      leftHtml: renderHtmlText(oldText),
      rightHtml: renderHtmlText(newText),
      isSame: true
    };
    inlineDiffCache.set(cacheKey, same);
    return same;
  }

  const oldChars = Array.from(oldText);
  const newChars = Array.from(newText);
  const minLength = Math.min(oldChars.length, newChars.length);

  let prefix = 0;
  while (prefix < minLength && oldChars[prefix] === newChars[prefix]) {
    prefix += 1;
  }

  let oldSuffix = oldChars.length - 1;
  let newSuffix = newChars.length - 1;
  while (
    oldSuffix >= prefix &&
    newSuffix >= prefix &&
    oldChars[oldSuffix] === newChars[newSuffix]
  ) {
    oldSuffix -= 1;
    newSuffix -= 1;
  }

  const oldPrefixText = oldChars.slice(0, prefix).join("");
  const oldMiddleText = oldChars.slice(prefix, oldSuffix + 1).join("");
  const oldSuffixText = oldChars.slice(oldSuffix + 1).join("");
  const newPrefixText = newChars.slice(0, prefix).join("");
  const newMiddleText = newChars.slice(prefix, newSuffix + 1).join("");
  const newSuffixText = newChars.slice(newSuffix + 1).join("");

  const result = {
    leftHtml:
      `${formatHtmlFragment(oldPrefixText)}` +
      `${oldMiddleText ? `<span class="inline-mark inline-mark-old">${formatHtmlFragment(oldMiddleText)}</span>` : ""}` +
      `${oldSuffixText ? formatHtmlFragment(oldSuffixText) : ""}`,
    rightHtml:
      `${formatHtmlFragment(newPrefixText)}` +
      `${newMiddleText ? `<span class="inline-mark inline-mark-new">${formatHtmlFragment(newMiddleText)}</span>` : ""}` +
      `${newSuffixText ? formatHtmlFragment(newSuffixText) : ""}`,
    isSame: false
  };

  if (!result.leftHtml) {
    result.leftHtml = `<span class="placeholder-text">（空）</span>`;
  }
  if (!result.rightHtml) {
    result.rightHtml = `<span class="placeholder-text">（空）</span>`;
  }

  inlineDiffCache.set(cacheKey, result);
  return result;
};

const comparisonOptions = computed(() =>
  topCandidates.value
    .filter(candidate => candidate.rank > 1)
    .map(candidate => ({
      label: `Top${candidate.rank}`,
      value: candidate.rank
    }))
);

const comparisonCandidate = computed(
  () =>
    topCandidates.value.find(candidate => candidate.rank === comparisonRank.value) ??
    topCandidates.value[1] ??
    null
);

watch(
  () => topCandidates.value.map(candidate => candidate.rank).join(","),
  () => {
    const firstComparable = topCandidates.value.find(candidate => candidate.rank > 1);
    if (!firstComparable) {
      comparisonRank.value = null;
      return;
    }

    const exists = topCandidates.value.some(
      candidate => candidate.rank === comparisonRank.value && candidate.rank > 1
    );
    comparisonRank.value = exists ? comparisonRank.value : firstComparable.rank;
  },
  { immediate: true }
);

const comparisonBaseRows = computed(() => {
  const first = topCandidates.value[0];
  const candidate = comparisonCandidate.value;
  if (!first || !candidate) return [];

  return [
    {
      key: "project",
      label: "项目",
      left: first.project,
      right: candidate.project
    },
    {
      key: "specification",
      label: "规格",
      left: first.specification,
      right: candidate.specification
    },
    {
      key: "acceptance",
      label: "验收标准",
      left: first.acceptance ?? "",
      right: candidate.acceptance ?? ""
    },
    {
      key: "remark",
      label: "备注",
      left: first.remark ?? "",
      right: candidate.remark ?? ""
    }
  ]
    .filter(row => row.left.length > 0 || row.right.length > 0)
    .map(row => {
      const diff = buildInlineDiffHtml(row.left, row.right);
      return {
        key: row.key,
        label: row.label,
        leftHtml: diff.leftHtml,
        rightHtml: diff.rightHtml,
        isSame: diff.isSame
      };
    });
});

const comparisonRows = computed(() => comparisonBaseRows.value);

const rawComparisonRows = computed(() => {
  if (!rawOnlyDiff.value) return comparisonBaseRows.value;
  return comparisonBaseRows.value.filter(row => !row.isSame);
});

const isComparedCandidate = (candidate: MatchCandidateOption) =>
  candidate.rank > 1 && candidate.rank === comparisonCandidate.value?.rank;

const handleSelectComparisonCandidate = (candidate: MatchCandidateOption) => {
  if (candidate.rank <= 1) return;
  comparisonRank.value = candidate.rank;
};

const isCandidateExpanded = (candidate: MatchCandidateOption) =>
  candidate.rank === 1 || isComparedCandidate(candidate);
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    title="匹配详情"
    width="920px"
    top="5vh"
    destroy-on-close
  >
    <el-scrollbar class="dialog-scroll">
      <template v-if="item">
        <div class="detail-layout">
          <div class="source-info">
            <h4>源数据</h4>
            <el-descriptions :column="2" border size="small">
              <el-descriptions-item label="项目">
                {{ item.sourceProject }}
              </el-descriptions-item>
              <el-descriptions-item label="规格">
                {{ item.sourceSpecification }}
              </el-descriptions-item>
              <el-descriptions-item label="置信度">
                <el-tag :type="getConfidenceClass(item.confidenceLevel)" size="small">
                  {{ getConfidenceText(item.confidenceLevel) }}
                </el-tag>
              </el-descriptions-item>
              <el-descriptions-item label="最佳得分">
                {{ item.bestMatch ? formatScore(item.bestMatch.score) : "-" }}
              </el-descriptions-item>
            </el-descriptions>
          </div>

          <div class="best-section">
            <h4>最佳匹配</h4>
            <template v-if="item.bestMatch">
              <el-descriptions :column="2" border size="small">
                <el-descriptions-item label="项目">
                  {{ item.bestMatch.project }}
                </el-descriptions-item>
                <el-descriptions-item label="规格">
                  {{ item.bestMatch.specification }}
                </el-descriptions-item>
                <el-descriptions-item label="匹配策略">
                  {{ getStrategyText(item.bestMatch.matchingStrategy) }}
                </el-descriptions-item>
                <el-descriptions-item label="验收标准">
                  {{ item.bestMatch.acceptance || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="最终得分">
                  {{ formatScore(item.bestMatch.score) }}
                </el-descriptions-item>
                <el-descriptions-item label="Embedding得分">
                  {{ formatScore(item.bestMatch.embeddingScore) }}
                </el-descriptions-item>
                <el-descriptions-item label="召回候选数">
                  {{ item.bestMatch.recalledCandidateCount || "-" }}
                </el-descriptions-item>
                <el-descriptions-item label="Top1/Top2分差">
                  {{ formatOptionalScore(item.bestMatch.scoreGap) }}
                </el-descriptions-item>
                <el-descriptions-item label="高歧义">
                  <el-tag
                    :type="item.bestMatch.isAmbiguous ? 'warning' : 'success'"
                    size="small"
                  >
                    {{ item.bestMatch.isAmbiguous ? "是" : "否" }}
                  </el-tag>
                </el-descriptions-item>
                <el-descriptions-item label="LLM复核得分">
                  {{ formatLlmScore(item.bestMatch.llmScore) }}
                </el-descriptions-item>
              </el-descriptions>

              <div v-if="item.bestMatch.rerankSummary" class="info-block">
                <div class="info-label">重排摘要</div>
                <div class="info-text">{{ item.bestMatch.rerankSummary }}</div>
              </div>

              <div class="info-block">
                <div class="info-label">LLM复核原因</div>
                <div class="info-text">
                  {{ item.bestMatch.llmReason || item.llmReviewDraft || "-" }}
                </div>
                <div class="info-label">LLM复核过程</div>
                <div class="info-text">
                  {{ item.bestMatch.llmCommentary || "-" }}
                </div>
                <div v-if="item.llmReviewError" class="info-error">
                  LLM复核失败：{{ item.llmReviewError }}
                </div>
              </div>
            </template>
            <el-empty v-else description="无匹配结果" :image-size="60" />
          </div>

          <div v-if="topCandidates.length > 0" class="candidate-section">
            <div class="candidate-header">
              <h4>候选对比</h4>
              <span>用于判断 Top1 与 Top2/Top3 为什么接近或拉开</span>
            </div>

            <div v-if="comparisonCandidate" class="diff-section">
              <div class="diff-header">
                <div>
                  <h5>Top1 差异高亮</h5>
                  <p>左侧固定为 Top1，右侧可切换 Top2 / Top3，支持字段视图和原文对照，也可直接点击下方候选卡切换</p>
                </div>
                <div class="diff-toolbar">
                  <el-radio-group
                    v-if="comparisonOptions.length > 1"
                    v-model="comparisonRank"
                    size="small"
                  >
                    <el-radio-button
                      v-for="option in comparisonOptions"
                      :key="option.value"
                      :label="option.value"
                    >
                      {{ option.label }}
                    </el-radio-button>
                  </el-radio-group>
                  <el-tag v-else type="info" effect="plain">
                    对比 Top{{ comparisonCandidate.rank }}
                  </el-tag>
                  <el-radio-group v-model="diffViewMode" size="small">
                    <el-radio-button label="raw">原文对照</el-radio-button>
                    <el-radio-button label="field">字段差异</el-radio-button>
                  </el-radio-group>
                </div>
              </div>

              <div
                v-if="diffViewMode === 'raw'"
                class="raw-diff-shell"
              >
                <div class="raw-diff-meta">
                  <div class="raw-diff-desc">
                    采用左右并排对照，绿色表示候选新增内容，红色表示 Top1 独有内容。
                  </div>
                  <el-switch
                    v-model="rawOnlyDiff"
                    inline-prompt
                    active-text="仅差异"
                    inactive-text="全部字段"
                  />
                </div>

                <div class="raw-diff-header">
                  <div class="raw-diff-header-spacer" />
                  <div class="raw-diff-header-title">
                    Top1 · 规格 {{ topCandidates[0]?.specId }}
                  </div>
                  <div class="raw-diff-header-title">
                    Top{{ comparisonCandidate.rank }} · 规格 {{ comparisonCandidate.specId }}
                  </div>
                </div>

                <div
                  v-if="rawComparisonRows.length > 0"
                  class="raw-diff-rows"
                >
                  <div
                    v-for="(row, index) in rawComparisonRows"
                    :key="`raw-${row.key}`"
                    class="raw-diff-row"
                    :class="{ 'diff-row-same': row.isSame }"
                  >
                    <div class="raw-line-cell">
                      <div class="raw-line-no">{{ index + 1 }}</div>
                      <div class="raw-line-label">{{ row.label }}</div>
                    </div>
                    <div class="raw-pane-cell">
                      <div class="raw-pane-inner">
                        <div class="raw-pane-label">{{ row.label }}</div>
                        <div class="raw-pane-content" v-html="row.leftHtml" />
                      </div>
                    </div>
                    <div class="raw-pane-cell">
                      <div class="raw-pane-inner">
                        <div class="raw-pane-label">{{ row.label }}</div>
                        <div class="raw-pane-content" v-html="row.rightHtml" />
                      </div>
                    </div>
                  </div>
                </div>
                <el-empty
                  v-else
                  description="当前 Top1 与该候选无字段差异"
                  :image-size="60"
                />
              </div>

              <div v-else class="diff-columns">
                <div class="diff-column">
                  <div class="diff-column-title">
                    Top1 · 规格 {{ topCandidates[0]?.specId }}
                  </div>
                </div>
                <div class="diff-column">
                  <div class="diff-column-title">
                    Top{{ comparisonCandidate.rank }} · 规格 {{ comparisonCandidate.specId }}
                  </div>
                </div>
              </div>

              <div v-if="diffViewMode === 'field'" class="diff-rows">
                <div
                  v-for="row in comparisonRows"
                  :key="row.key"
                  class="diff-row"
                  :class="{ 'diff-row-same': row.isSame }"
                >
                  <div class="diff-label">{{ row.label }}</div>
                  <div class="diff-cell">
                    <div class="diff-content" v-html="row.leftHtml" />
                  </div>
                  <div class="diff-cell">
                    <div class="diff-content" v-html="row.rightHtml" />
                  </div>
                </div>
              </div>
            </div>

            <div class="candidate-list">
              <el-card
                v-for="candidate in topCandidates"
                :key="candidate.rank"
                class="candidate-card"
                :class="{
                  'is-top1': candidate.rank === 1,
                  'is-compared': isComparedCandidate(candidate),
                  'is-clickable': candidate.rank > 1
                }"
                shadow="never"
                @click="handleSelectComparisonCandidate(candidate)"
              >
                <div class="candidate-top">
                  <div>
                    <div class="candidate-rank">
                      Top{{ candidate.rank }}
                      <span v-if="candidate.rank === 1" class="candidate-status candidate-status-top1">
                        当前最佳
                      </span>
                      <span
                        v-else-if="isComparedCandidate(candidate)"
                        class="candidate-status candidate-status-compare"
                      >
                        当前对比
                      </span>
                    </div>
                    <div class="candidate-title">
                      {{ candidate.project }} - {{ candidate.specification }}
                    </div>
                  </div>
                  <div class="candidate-score">
                    <strong>{{ formatScore(candidate.score) }}</strong>
                    <span>{{ getCandidateDelta(candidate) }}</span>
                  </div>
                </div>

                <div v-if="isCandidateExpanded(candidate)" class="candidate-detail">
                  <el-descriptions :column="2" border size="small">
                    <el-descriptions-item label="规格ID">
                      {{ candidate.specId }}
                    </el-descriptions-item>
                    <el-descriptions-item label="Embedding得分">
                      {{ formatScore(candidate.embeddingScore) }}
                    </el-descriptions-item>
                    <el-descriptions-item label="验收标准">
                      {{ candidate.acceptance || "-" }}
                    </el-descriptions-item>
                    <el-descriptions-item label="备注">
                      {{ candidate.remark || "-" }}
                    </el-descriptions-item>
                  </el-descriptions>

                  <div v-if="candidate.rerankSummary" class="info-block compact">
                    <div class="info-label">候选摘要</div>
                    <div class="info-text">{{ candidate.rerankSummary }}</div>
                  </div>

                  <div
                    v-if="getSortedScoreDetails(candidate).length > 0"
                    class="score-grid"
                  >
                    <div
                      v-for="[key, value] in getSortedScoreDetails(candidate)"
                      :key="`${candidate.rank}-${key}`"
                      class="score-chip"
                    >
                      <span>{{ getScoreLabel(key) }}</span>
                      <strong>{{ formatScore(value) }}</strong>
                    </div>
                  </div>
                </div>
                <div v-else class="candidate-collapsed">
                  <div class="candidate-collapsed-meta">
                    <span>规格ID {{ candidate.specId }}</span>
                    <span>Embedding {{ formatScore(candidate.embeddingScore) }}</span>
                  </div>
                  <div v-if="candidate.rerankSummary" class="candidate-collapsed-summary">
                    {{ candidate.rerankSummary }}
                  </div>
                  <div class="candidate-collapsed-tip">
                    点击卡片切换为当前对比项并展开详情
                  </div>
                </div>
              </el-card>
            </div>
          </div>
        </div>
      </template>
    </el-scrollbar>

    <template #footer>
      <el-button @click="dialogVisible = false">关闭</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.dialog-scroll {
  max-height: 72vh;
  padding-right: 4px;
}

.detail-layout {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.source-info h4,
.best-section h4,
.candidate-header h4 {
  margin: 0 0 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.candidate-header {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.candidate-header span {
  font-size: 12px;
  color: #6b7280;
}

.diff-section {
  margin-bottom: 14px;
  padding: 14px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #fcfdff 0%, #f7f9fc 100%);
}

.diff-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
}

.diff-header h5 {
  margin: 0;
  font-size: 14px;
  color: #111827;
}

.diff-header p {
  margin: 4px 0 0;
  font-size: 12px;
  color: #6b7280;
}

.diff-toolbar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 10px;
}

.diff-columns {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  margin-bottom: 8px;
}

.diff-column {
  min-width: 0;
}

.diff-column:first-child {
  visibility: hidden;
}

.diff-column-title {
  padding: 8px 10px;
  border-radius: 10px;
  background: #eef4ff;
  color: #1f2937;
  font-size: 12px;
  font-weight: 600;
}

.diff-rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.diff-row {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  align-items: stretch;
}

.diff-row-same .diff-cell {
  background: #f9fafb;
}

.diff-label {
  display: flex;
  align-items: flex-start;
  justify-content: flex-start;
  padding-top: 10px;
  font-size: 12px;
  color: #6b7280;
}

.diff-cell {
  min-width: 0;
  padding: 10px 12px;
  border-radius: 12px;
  border: 1px solid #e5e7eb;
  background: #fff;
}

.diff-content {
  font-size: 13px;
  color: #111827;
  line-height: 1.7;
  word-break: break-word;
}

.raw-diff-shell {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.raw-diff-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.raw-diff-desc {
  font-size: 12px;
  color: #6b7280;
}

.raw-diff-header {
  display: grid;
  grid-template-columns: 90px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
}

.raw-diff-header-spacer {
  min-height: 1px;
}

.raw-diff-header-title {
  padding: 8px 10px;
  border-radius: 10px;
  background: #eef4ff;
  color: #1f2937;
  font-size: 12px;
  font-weight: 600;
}

.raw-diff-rows {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.raw-diff-row {
  display: grid;
  grid-template-columns: 90px minmax(0, 1fr) minmax(0, 1fr);
  gap: 10px;
  align-items: stretch;
}

.raw-line-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
  align-items: flex-start;
  justify-content: flex-start;
  padding: 10px 8px;
  border-radius: 12px;
  background: #f3f4f6;
}

.raw-line-no {
  font-size: 12px;
  font-weight: 700;
  color: #374151;
}

.raw-line-label {
  font-size: 12px;
  color: #6b7280;
}

.raw-pane-cell {
  min-width: 0;
  border: 1px solid #e5e7eb;
  border-radius: 12px;
  background: #fff;
  overflow: hidden;
}

.raw-pane-inner {
  display: flex;
  flex-direction: column;
  min-height: 100%;
}

.raw-pane-label {
  padding: 8px 10px;
  border-bottom: 1px solid #eef2f7;
  background: #f8fafc;
  font-size: 12px;
  color: #6b7280;
}

.raw-pane-content {
  padding: 12px;
  font-family: Consolas, "Courier New", monospace;
  font-size: 13px;
  color: #111827;
  line-height: 1.75;
  white-space: normal;
  word-break: break-word;
}

.info-block {
  margin-top: 12px;
  padding: 12px 14px;
  border-radius: 10px;
  background: #f8fafc;
}

.info-block.compact {
  margin-top: 10px;
}

.info-label {
  font-size: 12px;
  color: #6b7280;
}

.info-text {
  margin-top: 4px;
  font-size: 13px;
  color: #374151;
  white-space: pre-wrap;
  line-height: 1.6;
}

.info-error {
  margin-top: 8px;
  font-size: 12px;
  color: #f56c6c;
}

.candidate-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.candidate-card {
  border-radius: 14px;
  transition:
    border-color 0.2s ease,
    background 0.2s ease,
    box-shadow 0.2s ease,
    transform 0.2s ease;
}

.candidate-card.is-top1 {
  border-color: #409eff;
  background: linear-gradient(180deg, #f8fbff 0%, #ffffff 100%);
}

.candidate-card.is-compared {
  border-color: #e6a23c;
  background: linear-gradient(180deg, #fffaf2 0%, #ffffff 100%);
}

.candidate-card.is-clickable {
  cursor: pointer;
}

.candidate-card.is-clickable:hover {
  border-color: #cbd5e1;
  box-shadow: 0 10px 24px rgba(15, 23, 42, 0.06);
  transform: translateY(-1px);
}

.candidate-card.is-compared.is-clickable:hover {
  border-color: #e6a23c;
}

.candidate-detail {
  display: flex;
  flex-direction: column;
}

.candidate-collapsed {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #f8fafc;
}

.candidate-collapsed-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  font-size: 12px;
  color: #4b5563;
}

.candidate-collapsed-summary {
  font-size: 13px;
  color: #374151;
  line-height: 1.6;
}

.candidate-collapsed-tip {
  font-size: 12px;
  color: #b45309;
}

.candidate-top {
  display: flex;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 12px;
}

.candidate-rank {
  font-size: 12px;
  color: #6b7280;
  margin-bottom: 6px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.candidate-status {
  display: inline-flex;
  align-items: center;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 11px;
  line-height: 1.4;
}

.candidate-status-top1 {
  background: rgba(64, 158, 255, 0.12);
  color: #1d4ed8;
}

.candidate-status-compare {
  background: rgba(230, 162, 60, 0.14);
  color: #b45309;
}

.candidate-title {
  line-height: 1.6;
  color: #111827;
}

.candidate-score {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  min-width: 110px;
}

.candidate-score strong {
  font-size: 18px;
  color: #111827;
}

.candidate-score span {
  font-size: 12px;
  color: #6b7280;
}

.score-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
  gap: 8px;
  margin-top: 12px;
}

.score-chip {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 10px;
  background: #f8fafc;
  font-size: 12px;
  color: #6b7280;
}

.score-chip strong {
  color: #111827;
  font-size: 13px;
}

:deep(.inline-mark) {
  padding: 0 2px;
  border-radius: 4px;
}

:deep(.inline-mark-old) {
  background: rgba(245, 108, 108, 0.18);
  color: #b42318;
}

:deep(.inline-mark-new) {
  background: rgba(103, 194, 58, 0.18);
  color: #166534;
}

:deep(.placeholder-text) {
  color: #9ca3af;
  font-style: italic;
}

@media (max-width: 900px) {
  .candidate-header,
  .candidate-top,
  .diff-header,
  .raw-diff-meta {
    flex-direction: column;
  }

  .candidate-score {
    align-items: flex-start;
  }

  .diff-columns,
  .diff-row,
  .raw-diff-header,
  .raw-diff-row {
    grid-template-columns: 1fr;
  }

  .diff-column:first-child,
  .raw-diff-header-spacer {
    display: none;
  }

  .diff-label,
  .raw-line-cell {
    padding-top: 0;
    font-weight: 600;
  }
}
</style>
