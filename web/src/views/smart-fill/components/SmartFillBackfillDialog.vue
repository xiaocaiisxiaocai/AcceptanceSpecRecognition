<script setup lang="ts">
import type { SmartFillBackfillCandidate } from "../composables/useSmartFillBackfillState";

defineProps<{
  visible: boolean;
  candidates: SmartFillBackfillCandidate[];
  selectedCount: number;
  backfillingSpecs: boolean;
  executing: boolean;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "toggleAll", checked: boolean): void;
  (e: "executeWithoutBackfill"): void;
  (e: "confirmBackfill"): void;
}>();

const handleToggleAll = (checked: string | number | boolean) => {
  emit("toggleAll", Boolean(checked));
};

const getBackfillCandidateRowKey = (row: SmartFillBackfillCandidate) =>
  `${row.tableIndex}:${row.rowIndex}`;

const formatFinalValue = (value: string | undefined) =>
  value === "" ? "（清空）" : value || "-";

const formatOriginalValue = (value: string | undefined) => value || "-";

const getOriginalAcceptance = (row: SmartFillBackfillCandidate) =>
  formatOriginalValue(row.originalAcceptance);

const getFinalAcceptance = (row: SmartFillBackfillCandidate) =>
  formatFinalValue(row.overrideAcceptance ?? row.originalAcceptance);

const getOriginalRemark = (row: SmartFillBackfillCandidate) =>
  formatOriginalValue(row.originalRemark);

const getFinalRemark = (row: SmartFillBackfillCandidate) =>
  formatFinalValue(row.overrideRemark ?? row.originalRemark);
</script>

<template>
  <el-dialog
    :model-value="visible"
    title="回填验收规格"
    width="1080px"
    align-center
    :close-on-click-modal="false"
    @update:model-value="emit('update:visible', $event)"
  >
    <div class="backfill-dialog">
      <div class="backfill-summary">
        <span>共 {{ candidates.length }} 条手动修改内容</span>
        <span>已选择 {{ selectedCount }} 条回填</span>
      </div>
      <el-table
        :data="candidates"
        border
        max-height="460"
        :row-key="getBackfillCandidateRowKey"
      >
        <el-table-column width="56" align="center">
          <template #header>
            <el-checkbox
              :model-value="
                candidates.length > 0 && selectedCount === candidates.length
              "
              :indeterminate="
                selectedCount > 0 && selectedCount < candidates.length
              "
              @change="handleToggleAll"
            />
          </template>
          <template #default="{ row }">
            <el-checkbox v-model="row.selected" />
          </template>
        </el-table-column>
        <el-table-column label="表格/行" width="110" align="center">
          <template #default="{ row }">
            {{ row.tableIndex + 1 }} / {{ row.rowIndex + 1 }}
          </template>
        </el-table-column>
        <el-table-column label="动作" width="120" align="center">
          <template #default="{ row }">
            <el-tag
              size="small"
              :type="row.actionType === 'update' ? 'warning' : 'success'"
            >
              {{ row.actionType === "update" ? "更新现有规格" : "新增规格" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="项目/规格" min-width="220">
          <template #default="{ row }">
            <div class="backfill-source">
              <div>{{ row.sourceProject }}</div>
              <div>{{ row.sourceSpecification }}</div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="验收标准（原值 → 新值）" min-width="260">
          <template #default="{ row }">
            <div class="backfill-comparison">
              <div class="backfill-change__old">
                <span class="backfill-change__label">原</span>
                <span class="backfill-change__value">
                  {{ getOriginalAcceptance(row) }}
                </span>
              </div>
              <div
                class="backfill-change__new"
                :class="{ 'is-changed': row.overrideAcceptance !== undefined }"
              >
                <span class="backfill-change__label">新</span>
                <span class="backfill-change__value">
                  {{ getFinalAcceptance(row) }}
                </span>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="备注（原值 → 新值）" min-width="220">
          <template #default="{ row }">
            <div class="backfill-comparison">
              <div class="backfill-change__old">
                <span class="backfill-change__label">原</span>
                <span class="backfill-change__value">
                  {{ getOriginalRemark(row) }}
                </span>
              </div>
              <div
                class="backfill-change__new"
                :class="{ 'is-changed': row.overrideRemark !== undefined }"
              >
                <span class="backfill-change__label">新</span>
                <span class="backfill-change__value">
                  {{ getFinalRemark(row) }}
                </span>
              </div>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <template #footer>
      <div class="backfill-footer">
        <el-button @click="emit('executeWithoutBackfill')">
          不回填，仅执行填充
        </el-button>
        <el-button
          type="primary"
          :loading="backfillingSpecs || executing"
          @click="emit('confirmBackfill')"
        >
          确认回填并执行填充
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped lang="scss">
.backfill-comparison {
  display: grid;
  gap: 5px;
  padding: 2px 0;
}

.backfill-change__old,
.backfill-change__new {
  display: grid;
  grid-template-columns: 20px minmax(0, 1fr);
  gap: 6px;
  align-items: start;
  line-height: 20px;
}

.backfill-change__label {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  font-size: 12px;
  border-radius: 4px;
}

.backfill-change__value {
  min-width: 0;
  overflow-wrap: anywhere;
}

.backfill-change__old {
  color: var(--el-text-color-secondary);

  .backfill-change__label {
    color: var(--el-text-color-secondary);
    background: var(--el-fill-color-light);
  }
}

.backfill-change__new {
  color: var(--el-text-color-primary);

  .backfill-change__label {
    color: var(--el-color-primary);
    background: var(--el-color-primary-light-9);
  }

  &.is-changed .backfill-change__value {
    font-weight: 600;
    color: var(--el-color-primary);
  }
}
</style>
