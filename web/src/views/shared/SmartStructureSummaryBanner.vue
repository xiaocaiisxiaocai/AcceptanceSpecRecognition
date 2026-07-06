<script setup lang="ts">
import { computed } from "vue";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import {
  createSmartStructureSummary,
  formatSmartStructurePercent
} from "./smart-structure-recognition";

const props = defineProps<{
  tables: SmartConfigRecognizedTable[];
  loading?: boolean;
}>();

const emit = defineEmits<{
  retry: [];
}>();

const summary = computed(() => createSmartStructureSummary(props.tables));
const alertType = computed(() => {
  if (summary.value.hasReject) return "error";
  if (summary.value.hasNeedConfirm) return "warning";
  if (summary.value.canAutoApplyAll) return "success";
  return "info";
});
const title = computed(() => {
  if (summary.value.total === 0) return "尚未执行智能结构识别";
  if (summary.value.hasReject) return "部分表格暂不可用";
  if (summary.value.hasNeedConfirm) return "识别完成，存在待确认表格";
  if (summary.value.canAutoApplyAll) return "识别完成，可直接进入下一步";
  return "识别完成";
});
</script>

<template>
  <div class="smart-structure-summary">
    <el-alert :type="alertType" :closable="false" show-icon>
      <template #title>
        <div class="summary-title">
          <span>{{ title }}</span>
          <el-button
            v-if="summary.total > 0"
            type="primary"
            link
            :loading="loading"
            @click="emit('retry')"
          >
            重新识别
          </el-button>
        </div>
      </template>
      <template #default>
        <div class="summary-metrics">
          <span>表格 {{ summary.total }}</span>
          <span>可直达 {{ summary.autoApply }}</span>
          <span>待确认 {{ summary.needConfirm }}</span>
          <span>不可用 {{ summary.reject }}</span>
          <span>推荐 {{ summary.recommended }}</span>
          <span>跳过 {{ summary.skip }}</span>
          <span>
            平均置信度
            {{ formatSmartStructurePercent(summary.averageConfidence) }}
          </span>
        </div>
      </template>
    </el-alert>
  </div>
</template>

<style scoped>
.smart-structure-summary {
  width: 100%;
}

.summary-title {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  min-width: 0;
}

.summary-metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 16px;
  align-items: center;
  margin-top: 6px;
  font-size: 13px;
  line-height: 1.7;
}
</style>
