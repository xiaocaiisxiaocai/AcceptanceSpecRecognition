<script setup lang="ts">
import { ref } from "vue";
import MatchConfig from "./MatchConfig.vue";
import type { MatchConfig as MatchConfigType } from "@/api/matching";

defineProps<{
  matchConfig: MatchConfigType;
  canLlmStream: boolean;
  previewBlockingMessage: string;
  previewBlockingHint: string;
  scopeSummary?: string;
}>();

const emit = defineEmits<{
  (e: "update:matchConfig", value: MatchConfigType): void;
  (
    e: "scopeChange",
    customerId?: number,
    processId?: number,
    machineModelId?: number
  ): void;
}>();

const matchConfigRef = ref<InstanceType<typeof MatchConfig> | null>(null);

defineExpose<{
  resetConfig?: () => void;
  getScope?: () => {
    customerId?: number;
    processId?: number;
    machineModelId?: number;
  };
  getServiceStatus?: () => {
    hasAvailableEmbeddingService: boolean;
    hasAvailableLlmService: boolean;
  };
}>({
  resetConfig: () => matchConfigRef.value?.resetConfig?.(),
  getScope: () =>
    matchConfigRef.value?.getScope?.() ?? {
      customerId: undefined,
      processId: undefined,
      machineModelId: undefined
    },
  getServiceStatus: () =>
    matchConfigRef.value?.getServiceStatus?.() ?? {
      hasAvailableEmbeddingService: true,
      hasAvailableLlmService: true
    }
});

const handleScopeChange = (
  customerId?: number,
  processId?: number,
  machineModelId?: number
) => {
  emit("scopeChange", customerId, processId, machineModelId);
};
</script>

<template>
  <div class="step-panel">
    <el-alert
      v-if="scopeSummary"
      type="info"
      :closable="false"
      show-icon
      :title="scopeSummary"
      class="scope-summary-alert"
    />
    <MatchConfig
      ref="matchConfigRef"
      :model-value="matchConfig"
      :allow-llm="canLlmStream"
      @update:model-value="emit('update:matchConfig', $event)"
      @scope-change="handleScopeChange"
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

<style scoped>
.scope-summary-alert {
  margin-bottom: 12px;
}
</style>
