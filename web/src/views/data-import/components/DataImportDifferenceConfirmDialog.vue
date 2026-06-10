<script setup lang="ts">
import DataImportDifferenceDialog from "./DataImportDifferenceDialog.vue";
import {
  differenceColumnDefs,
  formatDifferenceValue,
  formatScorePercent,
  getDifferenceMatchTypeLabel,
  getDifferenceMatchTypeTagType,
  hasAiDifferenceMeta,
  isDifferenceColumnChanged
} from "../dataImport.difference-formatters";
import type {
  DifferenceDecision,
  ImportPendingDifferenceWithTable
} from "../dataImport.types";

defineProps<{
  pendingDifferences: ImportPendingDifferenceWithTable[];
  pagedPendingDifferences: ImportPendingDifferenceWithTable[];
  differenceDecisionMap: Record<string, DifferenceDecision | undefined>;
  pendingImportDecisionCount: number;
  pendingPartialDecisionCount: number;
  pendingSkipDecisionCount: number;
  pendingUndecidedCount: number;
  pendingDifferenceDisplayStart: number;
  pendingDifferenceDisplayEnd: number;
  pendingDifferencePage: number;
  pendingDifferencePageSize: number;
  differenceDialogFooterTip: string;
  confirmDifferenceButtonText: string;
  importing: boolean;
}>();

const visible = defineModel<boolean>({ required: true });

const emit = defineEmits<{
  applyDecisionToAll: [decision: DifferenceDecision];
  updateDecision: [key: string, decision: DifferenceDecision];
  "update:pendingDifferencePage": [value: number];
  "update:pendingDifferencePageSize": [value: number];
  confirm: [];
}>();
</script>

<template>
  <DataImportDifferenceDialog v-model="visible">
    <div class="difference-dialog__summary">
      <el-alert
        type="warning"
        :closable="false"
        show-icon
        :title="`检测到 ${pendingDifferences.length} 条重复、差异或 AI 疑似重复数据，请逐条确认是否覆盖已有记录。`"
        description="左侧为数据库已有数据，右侧为本次待导入数据。选择“部分覆盖”时，仅更新验收和备注，不改项目和规格。"
      />
      <div class="difference-dialog__toolbar">
        <div class="difference-dialog__stats">
          <span>覆盖 {{ pendingImportDecisionCount }} 条</span>
          <span>部分覆盖 {{ pendingPartialDecisionCount }} 条</span>
          <span>跳过 {{ pendingSkipDecisionCount }} 条</span>
          <span>未确认 {{ pendingUndecidedCount }} 条</span>
        </div>
        <div class="difference-dialog__batch-actions">
          <el-button size="small" @click="emit('applyDecisionToAll', 'skip')">
            全部跳过
          </el-button>
          <el-button
            size="small"
            type="primary"
            plain
            @click="emit('applyDecisionToAll', 'partial')"
          >
            全部部分覆盖
          </el-button>
          <el-button
            size="small"
            type="warning"
            plain
            @click="emit('applyDecisionToAll', 'import')"
          >
            全部覆盖
          </el-button>
        </div>
      </div>
    </div>

    <div class="difference-dialog__list">
      <div
        v-for="item in pagedPendingDifferences"
        :key="item.key"
        class="difference-card"
      >
        <div class="difference-card__header">
          <div class="difference-card__meta">
            <span>表格 {{ item.tableIndex + 1 }}</span>
            <span>行号 {{ item.rowIndex }}</span>
            <el-tag
              :type="getDifferenceMatchTypeTagType(item.matchType)"
              effect="light"
              size="small"
            >
              {{ getDifferenceMatchTypeLabel(item.matchType) }}
            </el-tag>
            <el-tag
              v-if="item.isHighConfidence"
              type="success"
              effect="plain"
              size="small"
            >
              高置信
            </el-tag>
          </div>
          <el-tag
            v-if="differenceDecisionMap[item.key] === 'import'"
            type="warning"
            size="small"
          >
            已选择覆盖
          </el-tag>
          <el-tag
            v-else-if="differenceDecisionMap[item.key] === 'partial'"
            type="primary"
            size="small"
          >
            已选择部分覆盖
          </el-tag>
          <el-tag
            v-else-if="differenceDecisionMap[item.key] === 'skip'"
            type="info"
            size="small"
          >
            已选择跳过
          </el-tag>
          <el-tag v-else type="danger" size="small"> 待选择 </el-tag>
        </div>

        <div class="difference-card__content">
          <div
            v-if="hasAiDifferenceMeta(item)"
            class="difference-card__ai-meta"
          >
            <span v-if="item.embeddingScore !== undefined">
              Embedding：{{ formatScorePercent(item.embeddingScore) }}
            </span>
            <span v-if="item.llmScore !== undefined">
              LLM：{{ formatScorePercent(item.llmScore) }}
            </span>
            <span v-if="item.finalScore !== undefined">
              最终：{{ formatScorePercent(item.finalScore) }}
            </span>
          </div>
          <div v-if="item.reviewReason" class="difference-card__reason">
            {{ item.reviewReason }}
          </div>
          <div class="difference-sheet">
            <div class="difference-sheet__panel">
              <div class="difference-sheet__panel-title">数据库已有</div>
              <div class="difference-sheet__table">
                <div
                  v-for="column in differenceColumnDefs"
                  :key="`existing-head-${item.key}-${column.key}`"
                  class="difference-sheet__head"
                >
                  {{ column.label }}
                </div>
                <div
                  v-for="column in differenceColumnDefs"
                  :key="`existing-cell-${item.key}-${column.key}`"
                  class="difference-sheet__cell"
                  :class="{
                    'is-changed': isDifferenceColumnChanged(item, column)
                  }"
                >
                  {{ formatDifferenceValue(column.getExisting(item)) }}
                </div>
              </div>
            </div>

            <div
              class="difference-sheet__panel difference-sheet__panel--incoming"
            >
              <div class="difference-sheet__panel-title">本次导入</div>
              <div class="difference-sheet__table">
                <div
                  v-for="column in differenceColumnDefs"
                  :key="`incoming-head-${item.key}-${column.key}`"
                  class="difference-sheet__head"
                >
                  {{ column.label }}
                </div>
                <div
                  v-for="column in differenceColumnDefs"
                  :key="`incoming-cell-${item.key}-${column.key}`"
                  class="difference-sheet__cell"
                  :class="{
                    'is-changed': isDifferenceColumnChanged(item, column)
                  }"
                >
                  {{ formatDifferenceValue(column.getIncoming(item)) }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <div class="difference-card__footer">
          <el-radio-group
            :model-value="differenceDecisionMap[item.key]"
            size="small"
            @update:model-value="
              value =>
                emit('updateDecision', item.key, value as DifferenceDecision)
            "
          >
            <el-radio-button label="import">覆盖已有</el-radio-button>
            <el-radio-button label="partial">部分覆盖</el-radio-button>
            <el-radio-button label="skip">跳过</el-radio-button>
          </el-radio-group>
        </div>
      </div>
    </div>

    <div
      v-if="pendingDifferences.length > 0"
      class="difference-dialog__pagination"
    >
      <span class="difference-dialog__pagination-summary">
        当前显示 {{ pendingDifferenceDisplayStart }} -
        {{ pendingDifferenceDisplayEnd }} 条，共
        {{ pendingDifferences.length }} 条
      </span>
      <el-pagination
        :current-page="pendingDifferencePage"
        :page-size="pendingDifferencePageSize"
        background
        small
        layout="total, sizes, prev, pager, next"
        :page-sizes="[20, 50, 100]"
        :total="pendingDifferences.length"
        @update:current-page="
          value => emit('update:pendingDifferencePage', value)
        "
        @update:page-size="
          value => emit('update:pendingDifferencePageSize', value)
        "
      />
    </div>

    <template #footer>
      <div class="difference-dialog__footer">
        <span class="difference-dialog__footer-tip">
          {{ differenceDialogFooterTip }}
        </span>
        <div class="difference-dialog__footer-actions">
          <el-button @click="visible = false"> 稍后处理 </el-button>
          <el-button
            type="primary"
            :loading="importing"
            :disabled="pendingUndecidedCount > 0"
            @click="emit('confirm')"
          >
            {{ confirmDifferenceButtonText }}
          </el-button>
        </div>
      </div>
    </template>
  </DataImportDifferenceDialog>
</template>
