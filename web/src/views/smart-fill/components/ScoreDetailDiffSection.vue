<script setup lang="ts">
import { computed } from "vue";
import type { MatchPreviewItem } from "@/api/matching";
import type {
  ScoreDetailDiffRow,
  ScoreDetailDiffViewMode
} from "../composables/useScoreDetailDiff";

const props = defineProps<{
  item: MatchPreviewItem;
  topCandidates: any[];
  comparisonCandidate: any | null;
  comparisonOptions: Array<{ label: string; value: number }>;
  comparisonRank: number | null;
  diffViewMode: ScoreDetailDiffViewMode;
  rawOnlyDiff: boolean;
  sourceBestRows: ScoreDetailDiffRow[];
  comparisonRows: ScoreDetailDiffRow[];
  rawComparisonRows: ScoreDetailDiffRow[];
}>();

const emit = defineEmits<{
  (e: "update:comparisonRank", value: number | null): void;
  (e: "update:diffViewMode", value: ScoreDetailDiffViewMode): void;
  (e: "update:rawOnlyDiff", value: boolean): void;
}>();

const comparisonRankModel = computed({
  get: () => props.comparisonRank,
  set: value => emit("update:comparisonRank", value)
});

const diffViewModeModel = computed({
  get: () => props.diffViewMode,
  set: value => emit("update:diffViewMode", value)
});

const rawOnlyDiffModel = computed({
  get: () => props.rawOnlyDiff,
  set: value => emit("update:rawOnlyDiff", value)
});
</script>

<template>
  <div
    v-if="item.bestMatch && sourceBestRows.length > 0"
    class="best-section"
  >
    <h4>源项与最佳匹配差异</h4>
    <div class="diff-section">
      <div class="diff-columns">
        <div class="diff-column">
          <div class="diff-column-title">差异字段</div>
        </div>
        <div class="diff-column">
          <div class="diff-column-title">源项</div>
        </div>
        <div class="diff-column">
          <div class="diff-column-title">
            最佳匹配 · 规格 {{ item.bestMatch.specId }}
          </div>
        </div>
      </div>

      <div class="diff-rows">
        <div
          v-for="row in sourceBestRows"
          :key="`source-best-${row.key}`"
          class="diff-row"
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
            v-model="comparisonRankModel"
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
          <el-radio-group v-model="diffViewModeModel" size="small">
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
            v-model="rawOnlyDiffModel"
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
  </div>
</template>
