<script setup lang="ts">
import type { MatchCandidateOption } from "@/api/matching";
import {
  formatScore,
  getCandidateDelta,
  getDecisionTagType,
  getDecisionText,
  getIssueFieldText,
  getIssueSeverityText,
  getIssueTagType,
  getSelectionModeTagType,
  getSelectionModeText,
  getScoreLabel,
  getSortedScoreDetails
} from "./scoreDetail.formatters";

defineProps<{
  topCandidates: MatchCandidateOption[];
  isComparedCandidate: (candidate: MatchCandidateOption) => boolean;
  isCandidateExpanded: (candidate: MatchCandidateOption) => boolean;
  handleSelectComparisonCandidate: (candidate: MatchCandidateOption) => void;
}>();
</script>

<template>
  <div v-if="topCandidates.length > 0" class="candidate-list">
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
          <div class="candidate-tag-row">
            <el-tag
              size="small"
              effect="plain"
              :type="getSelectionModeTagType(candidate.selectionMode)"
            >
              {{ getSelectionModeText(candidate.selectionMode) }}
            </el-tag>
          </div>
          <div class="candidate-title">
            {{ candidate.project }} - {{ candidate.specification }}
          </div>
        </div>
        <div class="candidate-score">
          <strong>{{ formatScore(candidate.score) }}</strong>
          <span>{{ getCandidateDelta(candidate, topCandidates) }}</span>
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
          <el-descriptions-item label="决策">
            <el-tag
              :type="getDecisionTagType(candidate.decision)"
              size="small"
            >
              {{ getDecisionText(candidate.decision) }}
            </el-tag>
          </el-descriptions-item>
          <el-descriptions-item label="验收标准">
            {{ candidate.acceptance || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="备注">
            {{ candidate.remark || "-" }}
          </el-descriptions-item>
        </el-descriptions>

        <div
          v-if="candidate.issues?.length"
          class="info-block compact info-block--issue"
        >
          <div class="info-label">候选问题</div>
          <div class="issue-list">
            <div
              v-for="(issue, index) in candidate.issues"
              :key="`candidate-${candidate.rank}-issue-${index}-${issue.code}`"
              class="issue-card"
            >
              <div class="issue-card__header">
                <div class="issue-card__title">
                  {{ issue.message }}
                </div>
                <el-tag
                  size="small"
                  effect="plain"
                  :type="getIssueTagType(issue.severity)"
                >
                  {{ getIssueSeverityText(issue.severity) }}
                </el-tag>
              </div>
              <div class="issue-card__meta">
                字段：{{ getIssueFieldText(issue) }}
              </div>
              <div
                v-if="issue.sourceValue || issue.candidateValue"
                class="issue-card__meta"
              >
                源值：{{ issue.sourceValue || "-" }}；候选值：{{ issue.candidateValue || "-" }}
              </div>
              <div
                v-if="issue.suggestedAction"
                class="issue-card__action"
              >
                建议：{{ issue.suggestedAction }}
              </div>
            </div>
          </div>
        </div>

        <div
          v-if="candidate.evidenceSummary?.length"
          class="info-block compact"
        >
          <div class="info-label">候选证据</div>
          <div class="info-text">
            {{ candidate.evidenceSummary.join("；") }}
          </div>
        </div>

        <div
          v-if="candidate.conflictSummary?.length"
          class="info-block compact info-block--danger"
        >
          <div class="info-label">候选冲突</div>
          <div class="info-text">
            {{ candidate.conflictSummary.join("；") }}
          </div>
        </div>

        <div v-if="candidate.rerankSummary" class="info-block compact">
          <div class="info-label">候选摘要</div>
          <div class="info-text">{{ candidate.rerankSummary }}</div>
        </div>

        <div
          v-if="candidate.selectionSummary"
          class="info-block compact"
          :class="{ 'info-block--highlight': candidate.selectionMode === 'aiRerank' }"
        >
          <div class="info-label">选定摘要</div>
          <div class="info-text">{{ candidate.selectionSummary }}</div>
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
        <div
          v-if="candidate.selectionSummary"
          class="candidate-collapsed-summary"
          :class="{ 'candidate-collapsed-summary--highlight': candidate.selectionMode === 'aiRerank' }"
        >
          {{ candidate.selectionSummary }}
        </div>
        <div class="candidate-collapsed-tip">
          点击卡片切换为当前对比项并展开详情
        </div>
      </div>
    </el-card>
  </div>
</template>

<style scoped>
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

.candidate-collapsed-summary--highlight {
  color: #9a3412;
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

.candidate-tag-row {
  margin-bottom: 8px;
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

.info-block {
  margin-top: 10px;
  padding: 12px 14px;
  border-radius: 12px;
  background: #f8fafc;
}

.info-block--danger {
  background: #fff4f4;
}

.info-block--issue {
  background: #fff9f5;
}

.info-block--highlight {
  background: #fff8eb;
  border: 1px solid #fed7aa;
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

.issue-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 8px;
}
.issue-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.issue-card__title {
  font-size: 13px;
  font-weight: 600;
  line-height: 1.6;
}

.issue-card__title {
  color: #9a3412;
}

.issue-card__meta,
.issue-card__action {
  margin-top: 6px;
  font-size: 12px;
  line-height: 1.6;
}

.issue-card__meta {
  color: #7c2d12;
}

.issue-card__action {
  color: #b45309;
}

.issue-card {
  padding: 12px;
  border: 1px solid #fed7aa;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.78);
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

@media (max-width: 900px) {
  .candidate-top,
  .issue-card__header {
    flex-direction: column;
  }

  .candidate-score {
    align-items: flex-start;
  }
}
</style>
