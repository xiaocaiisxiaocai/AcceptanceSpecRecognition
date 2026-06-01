<script setup lang="ts">
import type { MatchPreviewSelection } from "./matchPreviewTable.types";

defineProps<{
  text: string;
  edited: boolean;
  selection?: MatchPreviewSelection | null;
}>();
</script>

<template>
  <div class="preview-cell">
    <span class="acceptance-text">
      {{ text }}
    </span>
    <el-tag v-if="edited" size="small" type="warning" effect="plain">
      已编辑
    </el-tag>
    <el-tag
      v-if="selection?.type === 'manual'"
      size="small"
      type="success"
      effect="plain"
    >
      已手工填写
    </el-tag>
  </div>
</template>
