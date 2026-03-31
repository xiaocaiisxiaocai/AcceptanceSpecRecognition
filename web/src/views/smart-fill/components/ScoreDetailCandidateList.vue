<script setup lang="ts">
import type { MatchCandidateOption } from "@/api/matching";
import {
  formatScore,
  getCandidateDelta,
  getDecisionTagType,
  getDecisionText,
  getEntityRelationTagType,
  getEntityRelationText,
  getIssueFieldText,
  getIssueSeverityText,
  getIssueTagType,
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
          <el-descriptions-item label="硬冲突">
            <el-tag
              :type="candidate.hasHardConflict ? 'danger' : 'success'"
              size="small"
            >
              {{ candidate.hasHardConflict ? "存在" : "无" }}
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
          v-if="candidate.entities?.length"
          class="info-block compact"
        >
          <div class="info-label">实体证据</div>
          <div class="entity-list">
            <div
              v-for="(entity, index) in candidate.entities"
              :key="`candidate-${candidate.rank}-entity-${index}-${entity.sourceValue}-${entity.candidateValue}`"
              class="entity-card"
            >
              <div class="entity-card__header">
                <div class="entity-card__title">
                  {{ entity.entityType || "实体" }}：{{ entity.sourceValue }} -> {{ entity.candidateValue }}
                </div>
                <el-tag
                  size="small"
                  effect="plain"
                  :type="getEntityRelationTagType(entity.relation)"
                >
                  {{ getEntityRelationText(entity.relation) }}
                </el-tag>
              </div>
              <div class="entity-card__meta">
                归一化：{{ entity.normalizedSourceValue || "-" }} / {{ entity.normalizedCandidateValue || "-" }}
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
</template>
