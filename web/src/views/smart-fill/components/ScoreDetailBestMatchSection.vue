<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import {
  formatLlmScore,
  formatOptionalScore,
  formatScore,
  getDecisionTagType,
  getDecisionText,
  getEntityRelationTagType,
  getEntityRelationText,
  getIssueFieldText,
  getIssueSeverityText,
  getIssueTagType
} from "./scoreDetail.formatters";

const props = defineProps<{
  item: MatchPreviewItem;
}>();

const bestMatchIssues = computed(() => props.item.bestMatch?.issues ?? []);
const bestMatchEntities = computed(() => props.item.bestMatch?.entities ?? []);
</script>

<template>
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
        <el-descriptions-item label="匹配引擎">
          统一多阶段证据驱动
        </el-descriptions-item>
        <el-descriptions-item label="验收标准">
          {{ item.bestMatch.acceptance || "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="最终决策">
          <el-tag
            :type="getDecisionTagType(item.bestMatch.decision)"
            size="small"
          >
            {{ getDecisionText(item.bestMatch.decision) }}
          </el-tag>
        </el-descriptions-item>
        <el-descriptions-item label="硬冲突">
          <el-tag
            :type="item.bestMatch.hasHardConflict ? 'danger' : 'success'"
            size="small"
          >
            {{ item.bestMatch.hasHardConflict ? "存在" : "无" }}
          </el-tag>
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

      <div
        v-if="bestMatchIssues.length > 0"
        class="info-block info-block--issue"
      >
        <div class="info-label">结构化问题</div>
        <div class="issue-list">
          <div
            v-for="(issue, index) in bestMatchIssues"
            :key="`best-issue-${index}-${issue.code}`"
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
        v-if="bestMatchEntities.length > 0"
        class="info-block"
      >
        <div class="info-label">实体证据</div>
        <div class="entity-list">
          <div
            v-for="(entity, index) in bestMatchEntities"
            :key="`best-entity-${index}-${entity.sourceValue}-${entity.candidateValue}`"
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
        v-if="item.bestMatch.evidenceSummary?.length"
        class="info-block"
      >
        <div class="info-label">证据摘要</div>
        <div class="info-text">
          {{ item.bestMatch.evidenceSummary.join("；") }}
        </div>
      </div>

      <div
        v-if="item.bestMatch.conflictSummary?.length"
        class="info-block info-block--danger"
      >
        <div class="info-label">冲突摘要</div>
        <div class="info-text">
          {{ item.bestMatch.conflictSummary.join("；") }}
        </div>
      </div>

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
</template>
