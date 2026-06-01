<script setup lang="ts">
import type { MatchPreviewItem } from "@/api/matching";
import { shouldShowReasonColumnForItem } from "./matchPreviewTable.formatters";

defineProps<{
  item: MatchPreviewItem;
}>();
</script>

<template>
  <div v-if="shouldShowReasonColumnForItem(item)" class="reason-cell">
    <div v-if="!item.hasMatch" class="reason-text">
      {{ item.noMatchReason || "未找到可匹配数据" }}
    </div>
    <div v-if="item.bestMatch?.conflictSummary?.length" class="reason-conflict">
      冲突：{{ item.bestMatch.conflictSummary.join("；") }}
    </div>
    <div v-if="item.llmReviewError" class="reason-text">
      复核异常：{{ item.llmReviewError }}
    </div>
  </div>
  <span v-else class="reason-none">-</span>
</template>
