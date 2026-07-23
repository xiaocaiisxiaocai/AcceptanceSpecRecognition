<script setup lang="ts">
import { ref } from "vue";
import MatchConfig from "./MatchConfig.vue";
import type { MatchConfig as MatchConfigType } from "@/api/matching";
import type { RuntimeAiSelectionRefreshResult } from "@/utils/runtime-ai-selection-loader";

defineProps<{
  matchConfig: MatchConfigType;
  canLlmStream: boolean;
  previewBlockingMessage: string;
  previewBlockingHint: string;
}>();

const emit = defineEmits<{
  (e: "update:matchConfig", value: MatchConfigType): void;
}>();

const matchConfigRef = ref<InstanceType<typeof MatchConfig> | null>(null);

defineExpose<{
  resetConfig?: () => void;
  refreshAiServices?: () => Promise<RuntimeAiSelectionRefreshResult>;
  getServiceStatus?: () => {
    hasAvailableEmbeddingService: boolean;
    hasAvailableLlmService: boolean;
  };
}>({
  resetConfig: () => matchConfigRef.value?.resetConfig?.(),
  refreshAiServices: () =>
    matchConfigRef.value?.refreshAiServices?.() ??
    Promise.resolve({ current: false, version: 0 }),
  getServiceStatus: () =>
    matchConfigRef.value?.getServiceStatus?.() ?? {
      hasAvailableEmbeddingService: false,
      hasAvailableLlmService: false
    }
});
</script>

<template>
  <div class="step-panel">
    <MatchConfig
      ref="matchConfigRef"
      :model-value="matchConfig"
      :allow-llm="canLlmStream"
      @update:model-value="emit('update:matchConfig', $event)"
    />
    <el-alert
      v-if="previewBlockingMessage"
      type="warning"
      :closable="false"
      show-icon
      :title="previewBlockingMessage"
      :description="previewBlockingHint"
      class="preview-blocking-alert"
    />
  </div>
</template>
