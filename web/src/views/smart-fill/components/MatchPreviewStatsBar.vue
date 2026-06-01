<script setup lang="ts">
import type {
  MatchPreviewScoreFilter,
  MatchPreviewStats
} from "./matchPreviewTable.formatters";

defineProps<{
  stats: MatchPreviewStats;
}>();

const scoreFilter = defineModel<MatchPreviewScoreFilter>("scoreFilter", {
  required: true
});
</script>

<template>
  <div class="stats-bar">
    <div class="stats-info">
      <span>共 {{ stats.total }} 行</span>
      <span class="divider">|</span>
      <span>已匹配 {{ stats.matched }} 行</span>
      <span class="divider">|</span>
      <span class="selected">已选择 {{ stats.selected }} 行</span>
      <span class="divider">|</span>
      <span class="ambiguous">高歧义 {{ stats.ambiguous }} 行</span>
    </div>
    <el-radio-group
      v-model="scoreFilter"
      size="small"
      class="score-filter"
    >
      <el-radio-button value="all">
        全部 ({{ stats.total }})
      </el-radio-button>
      <el-radio-button value="exactFillable">
        100%精确直达 ({{ stats.exactFillable }})
      </el-radio-button>
      <el-radio-button value="partialFillable">
        AI/普通可填充 ({{ stats.partialFillable }})
      </el-radio-button>
      <el-radio-button value="review">
        需要确认 ({{ stats.review }})
      </el-radio-button>
      <el-radio-button value="blocked">
        不建议填充 ({{ stats.blocked }})
      </el-radio-button>
      <el-radio-button value="unmatched">
        无匹配 ({{ stats.unmatched }})
      </el-radio-button>
    </el-radio-group>
  </div>
</template>
