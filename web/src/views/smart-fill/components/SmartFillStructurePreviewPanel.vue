<script setup lang="ts">
import { computed, ref, watch } from "vue";
import TablePreview from "@/views/data-import/components/TablePreview.vue";
import type { BatchTableConfigItem } from "./batchTableConfig.types";
import { buildSmartFillStructurePreviewRegions } from "../smartFill.structurePreview";

const props = defineProps<{
  fileId?: number;
  config?: BatchTableConfigItem;
  isExcelFile: boolean;
}>();

const regions = computed(() =>
  props.config
    ? buildSmartFillStructurePreviewRegions(props.config, props.isExcelFile)
    : []
);
const activeRegionKey = ref("");

watch(
  () => [
    props.config?.tableIndex,
    regions.value.map(region => region.key).join("|")
  ],
  () => {
    if (!regions.value.some(region => region.key === activeRegionKey.value)) {
      activeRegionKey.value = regions.value[0]?.key ?? "";
    }
  },
  { immediate: true }
);

const activeRegion = computed(
  () =>
    regions.value.find(region => region.key === activeRegionKey.value) ??
    regions.value[0]
);
const tableLabel = computed(
  () =>
    props.config?.tableInfo.name?.trim() ||
    (props.config ? `表格 ${props.config.tableIndex + 1}` : "")
);
</script>

<template>
  <section class="smart-fill-structure-preview-panel">
    <header class="smart-fill-structure-preview-panel__header">
      <div>
        <strong>待填充数据预览</strong>
        <span v-if="tableLabel">{{ tableLabel }}</span>
      </div>
      <el-tag size="small" effect="plain">源文件当前值</el-tag>
    </header>

    <el-tabs
      v-if="regions.length > 1"
      v-model="activeRegionKey"
      class="smart-fill-structure-preview-panel__tabs"
    >
      <el-tab-pane
        v-for="region in regions"
        :key="region.key"
        :label="region.label"
        :name="region.key"
      />
    </el-tabs>

    <div
      v-if="!fileId || !config || !activeRegion"
      class="smart-fill-structure-preview-panel__empty"
    >
      <el-empty description="暂无可预览的数据区域" />
    </div>
    <div v-else class="smart-fill-structure-preview-panel__table">
      <TablePreview
        :key="`${config.tableIndex}:${activeRegion.key}`"
        :file-id="fileId"
        :table-index="config.tableIndex"
        :header-row-index="activeRegion.headerRowIndex"
        :header-row-count="activeRegion.headerRowCount"
        :data-start-row-index="activeRegion.dataStartRowIndex"
        :data-end-row-index="activeRegion.dataEndRowIndex"
        :mapping="activeRegion.mapping"
      />
    </div>
  </section>
</template>

<style scoped>
.smart-fill-structure-preview-panel {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-width: 0;
  min-height: 0;
}

.smart-fill-structure-preview-panel__header {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  justify-content: space-between;
  min-height: 34px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--app-border);
}

.smart-fill-structure-preview-panel__header > div {
  display: flex;
  gap: 10px;
  align-items: baseline;
  min-width: 0;
}

.smart-fill-structure-preview-panel__header strong {
  font-size: 15px;
  color: var(--app-text-primary);
}

.smart-fill-structure-preview-panel__header span {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.smart-fill-structure-preview-panel__tabs {
  flex: 0 0 auto;
  margin-top: 4px;
}

.smart-fill-structure-preview-panel__tabs :deep(.el-tabs__header) {
  margin-bottom: 8px;
}

.smart-fill-structure-preview-panel__tabs :deep(.el-tabs__content) {
  display: none;
}

.smart-fill-structure-preview-panel__table {
  display: flex;
  flex: 1;
  min-width: 0;
  min-height: 0;
  padding-top: 10px;
}

.smart-fill-structure-preview-panel__table :deep(.table-preview) {
  height: 100%;
  min-height: 0;
}

.smart-fill-structure-preview-panel__empty {
  display: grid;
  flex: 1;
  place-items: center;
  min-height: 360px;
}
</style>
