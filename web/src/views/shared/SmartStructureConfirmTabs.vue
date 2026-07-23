<script setup lang="ts">
import { computed, watch } from "vue";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import SmartStructureConfirmCard from "./SmartStructureConfirmCard.vue";
import type { TableInfo } from "@/api/document";
import { sortSmartStructureTablesByIndex } from "./smart-structure-recognition";

const props = withDefaults(
  defineProps<{
    tables: SmartConfigRecognizedTable[];
    tableInfos?: TableInfo[];
    activeTableIndex?: number;
    selectedTableIndexes: number[];
    selectableTableIndexes: number[];
    selectionDisabledReasons?: Record<number, string>;
    selectionPendingReasons?: Record<number, string>;
    fileId?: number;
    customerId?: number;
    confirmingTableIndex?: number | null;
    readyLabel?: string;
    unavailableLabel?: string;
    isExcelFile?: boolean;
    showConfirmAction?: boolean;
    interactionLocked?: boolean;
  }>(),
  {
    tableInfos: () => [],
    selectionDisabledReasons: () => ({}),
    selectionPendingReasons: () => ({}),
    confirmingTableIndex: null,
    readyLabel: "可直达",
    unavailableLabel: "不可用",
    isExcelFile: true,
    showConfirmAction: true,
    interactionLocked: false
  }
);

const emit = defineEmits<{
  "update:activeTableIndex": [value: number | undefined];
  confirm: [
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest
  ];
  "draft-change": [
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest | null
  ];
  advanced: [table: SmartConfigRecognizedTable];
  "update:tableSelected": [
    table: SmartConfigRecognizedTable,
    selected: boolean
  ];
}>();

const tabItems = computed(() => sortSmartStructureTablesByIndex(props.tables));
const selectedTableIndexSet = computed(
  () => new Set(props.selectedTableIndexes)
);
const selectableTableIndexSet = computed(
  () => new Set(props.selectableTableIndexes)
);

const activeTableIndexModel = computed({
  get: () => props.activeTableIndex,
  set: value => emit("update:activeTableIndex", value)
});

watch(
  tabItems,
  tables => {
    if (!tables.length) {
      emit("update:activeTableIndex", undefined);
      return;
    }

    if (tables.some(table => table.tableIndex === props.activeTableIndex)) {
      return;
    }

    emit(
      "update:activeTableIndex",
      tables.find(table => table.decision === "NeedConfirm")?.tableIndex ??
        tables[0].tableIndex
    );
  },
  { immediate: true }
);
</script>

<template>
  <el-tabs
    v-if="tabItems.length > 0"
    v-model="activeTableIndexModel"
    class="smart-structure-confirm-tabs"
  >
    <el-tab-pane
      v-for="table in tabItems"
      :key="table.tableIndex"
      :name="table.tableIndex"
    >
      <template #label>
        <span class="smart-structure-confirm-tab-label">
          <span class="smart-structure-confirm-tab-name">
            {{ table.tableName || `工作表 ${table.tableIndex + 1}` }}
          </span>
          <span
            class="smart-structure-confirm-tab-status"
            :class="{
              'is-ready': table.decision === 'AutoApply',
              'is-unavailable': table.decision === 'Reject'
            }"
          >
            {{
              table.decision === "AutoApply"
                ? readyLabel
                : table.decision === "Reject"
                  ? unavailableLabel
                  : "待确认"
            }}
          </span>
        </span>
      </template>

      <SmartStructureConfirmCard
        :table="table"
        :table-info="tableInfos.find(item => item.index === table.tableIndex)"
        :file-id="fileId"
        :customer-id="customerId"
        :confirming="confirmingTableIndex === table.tableIndex"
        :confirmation-locked="confirmingTableIndex != null"
        :interaction-locked="interactionLocked"
        :show-confirm-action="showConfirmAction"
        :is-excel-file="isExcelFile"
        :import-selected="selectedTableIndexSet.has(table.tableIndex)"
        :import-selectable="selectableTableIndexSet.has(table.tableIndex)"
        :selection-disabled-reason="selectionDisabledReasons[table.tableIndex]"
        :selection-pending-reason="selectionPendingReasons[table.tableIndex]"
        @confirm="request => emit('confirm', table, request)"
        @draft-change="request => emit('draft-change', table, request)"
        @advanced="emit('advanced', table)"
        @update:import-selected="
          selected => emit('update:tableSelected', table, selected)
        "
      />
    </el-tab-pane>
  </el-tabs>
</template>

<style scoped>
.smart-structure-confirm-tabs {
  min-width: 0;
  margin-top: 12px;
}

.smart-structure-confirm-tabs :deep(.el-tabs__header) {
  margin: 0 0 10px;
}

.smart-structure-confirm-tab-label {
  display: inline-flex;
  gap: 6px;
  align-items: center;
  max-width: 210px;
}

.smart-structure-confirm-tab-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.smart-structure-confirm-tab-status {
  flex: none;
  font-size: 11px;
  color: var(--app-warning);
}

.smart-structure-confirm-tab-status.is-ready {
  color: var(--app-success);
}

.smart-structure-confirm-tab-status.is-unavailable {
  color: var(--app-text-secondary);
}
</style>
