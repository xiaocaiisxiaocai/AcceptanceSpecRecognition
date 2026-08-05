<script setup lang="ts">
import { computed } from "vue";
import type {
  SmartFillBackfillCandidate,
  SmartFillSpecWriteDecision
} from "../smartFillBackfill.types";

const props = defineProps<{
  visible: boolean;
  candidates: SmartFillBackfillCandidate[];
  backfillingSpecs: boolean;
  executing: boolean;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "setAllDecision", decision: SmartFillSpecWriteDecision): void;
  (e: "executeWithoutBackfill"): void;
  (e: "confirmBackfill"): void;
}>();

const getBackfillCandidateRowKey = (row: SmartFillBackfillCandidate) =>
  `${row.tableIndex}:${row.rowIndex}`;

const decisionCounts = computed(() => ({
  overwrite: props.candidates.filter(item => item.decision === "overwrite")
    .length,
  create: props.candidates.filter(item => item.decision === "create").length,
  skip: props.candidates.filter(item => item.decision === "skip").length
}));

const categoryCounts = computed(() => ({
  fillable: props.candidates.filter(item => item.category === "fillable")
    .length,
  review: props.candidates.filter(item => item.category === "review").length
}));

const formatFinalValue = (value: string | undefined) =>
  value === "" ? "（清空）" : value || "-";

const formatOriginalValue = (value: string | undefined) => value || "-";

const getFinalAcceptance = (row: SmartFillBackfillCandidate) =>
  formatFinalValue(row.overrideAcceptance ?? row.originalAcceptance);

const getFinalRemark = (row: SmartFillBackfillCandidate) =>
  formatFinalValue(row.overrideRemark ?? row.originalRemark);
</script>

<template>
  <el-dialog
    :model-value="visible"
    title="确认验收规格写库方式"
    width="min(1360px, 95vw)"
    align-center
    :close-on-click-modal="false"
    @update:model-value="emit('update:visible', $event)"
  >
    <div class="backfill-dialog">
      <div class="backfill-toolbar">
        <div class="backfill-summary">
          <strong>共 {{ candidates.length }} 条</strong>
          <span>AI/普通可填充 {{ categoryCounts.fillable }}</span>
          <span>需要确认 {{ categoryCounts.review }}</span>
          <el-divider direction="vertical" />
          <span>覆盖 {{ decisionCounts.overwrite }}</span>
          <span>增加 {{ decisionCounts.create }}</span>
          <span>跳过 {{ decisionCounts.skip }}</span>
        </div>
        <div class="backfill-batch-actions">
          <span>批量设置</span>
          <el-button size="small" @click="emit('setAllDecision', 'overwrite')">
            全部覆盖
          </el-button>
          <el-button size="small" @click="emit('setAllDecision', 'create')">
            全部增加
          </el-button>
          <el-button size="small" @click="emit('setAllDecision', 'skip')">
            全部跳过
          </el-button>
        </div>
      </div>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="“跳过写库”只是不修改验收规格资料库，当前文件仍会照常填充。"
      />

      <el-table
        :data="candidates"
        border
        max-height="520"
        :row-key="getBackfillCandidateRowKey"
      >
        <el-table-column label="Sheet / 行" width="130" align="center">
          <template #default="{ row }">
            <div class="backfill-location">
              <strong>{{ row.sheetName }}</strong>
              <span>第 {{ row.rowIndex + 1 }} 行</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="分类" width="120" align="center">
          <template #default="{ row }">
            <el-tag
              size="small"
              :type="row.category === 'fillable' ? 'success' : 'warning'"
            >
              {{ row.category === "fillable" ? "AI/普通可填充" : "需要确认" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="项目 / 规格（原 → 当前）" min-width="300">
          <template #default="{ row }">
            <div class="backfill-field-pair">
              <div class="backfill-comparison">
                <div class="backfill-change__old">
                  <span class="backfill-change__label">原</span>
                  <span class="backfill-change__value">
                    {{ formatOriginalValue(row.originalProject) }}
                  </span>
                </div>
                <div class="backfill-change__new is-changed">
                  <span class="backfill-change__label">新</span>
                  <span class="backfill-change__value">
                    {{ formatFinalValue(row.sourceProject) }}
                  </span>
                </div>
              </div>
              <div class="backfill-comparison">
                <div class="backfill-change__old">
                  <span class="backfill-change__label">原</span>
                  <span class="backfill-change__value">
                    {{ formatOriginalValue(row.originalSpecification) }}
                  </span>
                </div>
                <div class="backfill-change__new is-changed">
                  <span class="backfill-change__label">新</span>
                  <span class="backfill-change__value">
                    {{ formatFinalValue(row.sourceSpecification) }}
                  </span>
                </div>
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="验收标准（原 → 当前）" min-width="250">
          <template #default="{ row }">
            <div class="backfill-comparison">
              <div class="backfill-change__old">
                <span class="backfill-change__label">原</span>
                <span class="backfill-change__value">
                  {{ formatOriginalValue(row.originalAcceptance) }}
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
        <el-table-column label="备注（原 → 当前）" min-width="220">
          <template #default="{ row }">
            <div class="backfill-comparison">
              <div class="backfill-change__old">
                <span class="backfill-change__label">原</span>
                <span class="backfill-change__value">
                  {{ formatOriginalValue(row.originalRemark) }}
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
        <el-table-column
          label="写入资料库"
          width="250"
          align="center"
          fixed="right"
        >
          <template #default="{ row }">
            <el-radio-group v-model="row.decision" size="small">
              <el-radio-button value="overwrite">覆盖已有</el-radio-button>
              <el-radio-button value="create">增加一条</el-radio-button>
              <el-radio-button value="skip">跳过写库</el-radio-button>
            </el-radio-group>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <template #footer>
      <div class="backfill-footer">
        <el-button @click="emit('executeWithoutBackfill')">
          全部跳过并执行填充
        </el-button>
        <el-button
          type="primary"
          :loading="backfillingSpecs || executing"
          @click="emit('confirmBackfill')"
        >
          按当前选择继续填充
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped lang="scss">
.backfill-dialog {
  display: grid;
  gap: 12px;
}

.backfill-toolbar,
.backfill-summary,
.backfill-batch-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.backfill-toolbar {
  flex-wrap: wrap;
  justify-content: space-between;
}

.backfill-summary,
.backfill-batch-actions {
  color: var(--el-text-color-secondary);
}

.backfill-location,
.backfill-field-pair {
  display: grid;
  gap: 4px;
}

.backfill-location span {
  color: var(--el-text-color-secondary);
}

.backfill-field-pair > :first-child {
  padding-bottom: 5px;
  border-bottom: 1px dashed var(--el-border-color-lighter);
}

.backfill-comparison {
  display: grid;
  gap: 4px;
  padding: 1px 0;
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

.backfill-footer {
  display: flex;
  justify-content: flex-end;
}
</style>
