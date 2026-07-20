<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import type { TableInfo } from "@/api/document";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import SmartStructureRangeEditorDrawer from "./SmartStructureRangeEditorDrawer.vue";
import {
  buildSmartConfigConfirmRequest,
  canConfirmSmartStructureTable,
  formatSmartStructurePercent,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel,
  getSmartStructureIssueTagType,
  getSmartStructureRecommendationTag,
  getSmartStructureSourceLabel,
  getSmartStructureTableKindLabel,
  needsManualStructureFallback,
  countSmartStructureRegionRows,
  resolveSmartStructureRegionEndRowIndex,
  validateSmartStructureRegions
} from "./smart-structure-recognition";

const props = withDefaults(
  defineProps<{
    table: SmartConfigRecognizedTable;
    tableInfo?: TableInfo;
    fileId?: number;
    customerId?: number;
    confirming?: boolean;
    confirmationLocked?: boolean;
    readonly?: boolean;
    importSelected?: boolean;
    importSelectable?: boolean;
    selectionDisabledReason?: string;
    selectionPendingReason?: string;
    isExcelFile?: boolean;
    confirmActionLabel?: string;
    showConfirmAction?: boolean;
    interactionLocked?: boolean;
  }>(),
  {
    confirmationLocked: false,
    isExcelFile: true,
    confirmActionLabel: "确认并学习",
    showConfirmAction: true,
    interactionLocked: false
  }
);

const emit = defineEmits<{
  confirm: [request: SmartConfigConfirmRequest];
  "draft-change": [request: SmartConfigConfirmRequest | null];
  advanced: [table: SmartConfigRecognizedTable];
  "update:importSelected": [value: boolean];
}>();

const rangeEditorVisible = ref(false);
const editableRegions = ref<SmartConfigRecognizedRegion[]>([]);
const controlsLocked = computed(() =>
  Boolean(props.readonly || props.confirmationLocked || props.interactionLocked)
);

const resetState = () => {
  editableRegions.value = (
    props.table.regions?.length
      ? props.table.regions
      : [
          {
            regionId: `table-${props.table.tableIndex}-region-0`,
            regionIndex: 0,
            headers: props.table.headers,
            headerRowIndex: props.table.headerRowIndex,
            headerRowCount: props.table.headerRowCount,
            dataStartRowIndex: props.table.dataStartRowIndex,
            dataEndRowIndex: props.table.dataEndRowIndex,
            projectColumnIndex: props.table.projectColumnIndex,
            specificationColumnIndex: props.table.specificationColumnIndex,
            acceptanceColumnIndex: props.table.acceptanceColumnIndex,
            remarkColumnIndex: props.table.remarkColumnIndex,
            isSpecificationOnly: props.table.isSpecificationOnly,
            confidence: props.table.confidence,
            source: props.table.source,
            decision: props.table.decision,
            fields: props.table.fields
          }
        ]
  ).map(region => ({ ...region, headers: [...region.headers] }));
  rangeEditorVisible.value = false;
};

watch(() => props.table, resetState, { immediate: true });
watch(controlsLocked, locked => {
  if (locked) rangeEditorVisible.value = false;
});

const decisionTag = computed(() =>
  getSmartStructureDecisionTag(props.table.decision)
);
const recommendationTag = computed(() =>
  getSmartStructureRecommendationTag(props.table.recommendation)
);
const tableKindLabel = computed(() =>
  getSmartStructureTableKindLabel(props.table.tableKind)
);

const tableTitle = computed(
  () => props.table.tableName || `表格 ${props.table.tableIndex + 1}`
);

const toExcelColumnLabel = (columnNumber: number) => {
  let value = Math.max(1, columnNumber);
  let label = "";
  while (value > 0) {
    value -= 1;
    label = String.fromCharCode(65 + (value % 26)) + label;
    value = Math.floor(value / 26);
  }
  return label;
};

const formatColumnCoordinate = (index: number) =>
  props.isExcelFile
    ? toExcelColumnLabel((props.tableInfo?.usedRangeStartColumn ?? 1) + index)
    : `第 ${index + 1} 列`;

const activeRegions = computed(() => editableRegions.value);
const displayHeaders = computed(
  () => activeRegions.value[0]?.headers ?? props.table.headers
);

const handleRangesSave = (regions: SmartConfigRecognizedRegion[]) => {
  if (regions.length === 0) return;
  editableRegions.value = regions.map(region => ({
    ...region,
    headers: [...region.headers]
  }));
};

const formatColumnRange = (
  columnIndex: number | null | undefined,
  startRowIndex: number,
  endRowIndex: number | null | undefined
) => {
  if (columnIndex == null) return "-";
  const startRow = (props.tableInfo?.usedRangeStartRow ?? 1) + startRowIndex;
  const resolvedEndRowIndex = resolveSmartStructureRegionEndRowIndex(
    { dataStartRowIndex: startRowIndex, dataEndRowIndex: endRowIndex },
    props.tableInfo
  );
  const endRow =
    (props.tableInfo?.usedRangeStartRow ?? 1) + resolvedEndRowIndex;
  if (!props.isExcelFile) {
    return `第 ${columnIndex + 1} 列（第 ${startRow}–${endRow} 行）`;
  }
  const column = toExcelColumnLabel(
    (props.tableInfo?.usedRangeStartColumn ?? 1) + columnIndex
  );
  return `${column}${startRow}:${column}${endRow}`;
};

const rangeSummaryFields = computed(() =>
  [
    { label: "项目列", key: "projectColumnIndex" },
    { label: "规格列", key: "specificationColumnIndex" },
    { label: "验收列", key: "acceptanceColumnIndex" },
    { label: "备注列", key: "remarkColumnIndex" }
  ].map(field => ({
    label: field.label,
    ranges: activeRegions.value
      .map(region =>
        formatColumnRange(
          region[field.key as keyof typeof region] as number | null | undefined,
          region.dataStartRowIndex,
          region.dataEndRowIndex
        )
      )
      .filter(range => range !== "-"),
    emptyText:
      field.key === "projectColumnIndex" &&
      activeRegions.value.every(region => region.isSpecificationOnly)
        ? "仅规格表"
        : "未识别"
  }))
);

const regionSummaryItems = computed(() =>
  activeRegions.value.map(region => {
    const headerStartRow =
      (props.tableInfo?.usedRangeStartRow ?? 1) + region.headerRowIndex;
    const headerEndRow =
      headerStartRow + Math.max(1, region.headerRowCount) - 1;
    return {
      id: region.regionId,
      label: `区域 ${region.regionIndex + 1}`,
      headerRange:
        headerStartRow === headerEndRow
          ? `第 ${headerStartRow} 行`
          : `第 ${headerStartRow}–${headerEndRow} 行`
    };
  })
);

const effectiveRowCount = computed(() =>
  countSmartStructureRegionRows(activeRegions.value, props.tableInfo)
);
const ignoredRowCount = computed(() =>
  Math.max(
    0,
    (props.tableInfo?.rowCount ?? effectiveRowCount.value) -
      effectiveRowCount.value
  )
);

const allRegionsConfirmable = computed(() =>
  activeRegions.value.every(
    region =>
      (region.isSpecificationOnly || region.projectColumnIndex != null) &&
      region.specificationColumnIndex != null &&
      region.acceptanceColumnIndex != null
  )
);

const structureValidationError = computed(() =>
  validateSmartStructureRegions(activeRegions.value, props.tableInfo)
);

const importSwitchText = computed(() =>
  props.importSelected ? "参与导入" : "不导入"
);

const visibleIssues = computed(() => {
  const seen = new Set<string>();
  return [
    ...(props.table.issues ?? []),
    ...activeRegions.value.flatMap(region => region.issues ?? [])
  ]
    .filter(issue => {
      const key = `${issue.code}-${issue.field ?? ""}-${issue.message}`;
      if (seen.has(key)) return false;
      seen.add(key);
      return true;
    })
    .slice(0, 4);
});
const semanticRecallSuggestions = computed(
  () => props.table.semanticRecallSuggestions?.slice(0, 6) ?? []
);
const showRecognitionEvidence = computed(
  () =>
    props.table.decision !== "AutoApply" ||
    props.table.confidence < 0.8 ||
    visibleIssues.value.length > 0 ||
    semanticRecallSuggestions.value.length > 0
);
const showAdvancedFallback = computed(() =>
  needsManualStructureFallback(props.table)
);

const compactRegionStructure = (region: {
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number | null;
  projectColumnIndex?: number | null;
  specificationColumnIndex?: number | null;
  acceptanceColumnIndex?: number | null;
  remarkColumnIndex?: number | null;
  isSpecificationOnly: boolean;
}) => ({
  headerRowIndex: region.headerRowIndex,
  headerRowCount: region.headerRowCount,
  dataStartRowIndex: region.dataStartRowIndex,
  dataEndRowIndex: region.dataEndRowIndex ?? null,
  projectColumnIndex: region.projectColumnIndex ?? null,
  specificationColumnIndex: region.specificationColumnIndex ?? null,
  acceptanceColumnIndex: region.acceptanceColumnIndex ?? null,
  remarkColumnIndex: region.remarkColumnIndex ?? null,
  isSpecificationOnly: region.isSpecificationOnly
});

const originalRegionStructures = computed(() =>
  (props.table.regions?.length ? props.table.regions : [props.table]).map(
    compactRegionStructure
  )
);

const regionsHaveChanges = computed(
  () =>
    JSON.stringify(activeRegions.value.map(compactRegionStructure)) !==
    JSON.stringify(originalRegionStructures.value)
);

const hasStructureChanges = computed(() => regionsHaveChanges.value);

const canConfirm = computed(() =>
  canConfirmSmartStructureTable({
    readonly: Boolean(props.readonly),
    confirmationLocked: Boolean(
      props.confirmationLocked || props.interactionLocked
    ),
    customerId: props.customerId,
    allRegionsConfirmable: allRegionsConfirmable.value,
    structureValidationError: structureValidationError.value,
    decision: props.table.decision,
    hasStructureChanges: hasStructureChanges.value
  })
);

const confirmDisabledReason = computed(() => {
  if (canConfirm.value || props.readonly || controlsLocked.value) return "";
  if (props.table.decision === "Reject" && !hasStructureChanges.value) {
    return "识别结果不可用，请先调整范围或列映射后再确认";
  }
  return structureValidationError.value || "请先补齐必填字段后再确认";
});
const confirmDisabledReasonId = computed(
  () => `smart-structure-confirm-reason-${props.table.tableIndex}`
);

const buildDraftRequest = (): SmartConfigConfirmRequest | null => {
  if (!props.customerId || !allRegionsConfirmable.value) return null;

  try {
    return buildSmartConfigConfirmRequest(
      props.customerId,
      {
        ...props.table,
        regions: activeRegions.value
      },
      {
        fileId: props.fileId,
        userModifiedStructure: hasStructureChanges.value
      }
    );
  } catch {
    return null;
  }
};

watch(
  [
    () => props.customerId,
    () => props.fileId,
    () => props.table,
    activeRegions,
    hasStructureChanges
  ],
  () => emit("draft-change", buildDraftRequest()),
  { deep: true, immediate: true }
);

const emitConfirm = () => {
  if (structureValidationError.value) {
    ElMessage.warning(structureValidationError.value);
    return;
  }

  if (!props.customerId || !allRegionsConfirmable.value) {
    return;
  }

  emit(
    "confirm",
    buildSmartConfigConfirmRequest(
      props.customerId,
      {
        ...props.table,
        regions: activeRegions.value
      },
      {
        fileId: props.fileId,
        userModifiedStructure: hasStructureChanges.value
      }
    )
  );
};
</script>

<template>
  <section class="smart-structure-card">
    <div class="card-header">
      <div class="card-title">
        <el-checkbox
          :model-value="importSelected"
          :disabled="controlsLocked || importSelectable === false"
          :aria-label="tableTitle + '：' + importSwitchText"
          @update:model-value="
            value => emit('update:importSelected', Boolean(value))
          "
        >
          {{ importSwitchText }}
        </el-checkbox>
        <span>{{ tableTitle }}</span>
        <el-tag size="small" :type="decisionTag.type" effect="plain">
          {{ decisionTag.text }}
        </el-tag>
        <el-tag size="small" :type="recommendationTag.type" effect="plain">
          {{ recommendationTag.text }}
        </el-tag>
        <el-tag size="small" type="info" effect="plain">
          {{ tableKindLabel }}
        </el-tag>
      </div>
      <div v-if="showRecognitionEvidence" class="card-meta">
        <span>{{ getSmartStructureSourceLabel(table.source) }}</span>
        <span>置信度 {{ formatSmartStructurePercent(table.confidence) }}</span>
        <span>
          排序分 {{ formatSmartStructurePercent(table.rankingScore) }}
        </span>
      </div>
    </div>

    <div
      v-if="!readonly && importSelectable === false && selectionDisabledReason"
      class="selection-disabled-hint"
    >
      暂不可导入：{{ selectionDisabledReason }}
    </div>

    <div
      v-if="!readonly && importSelected && selectionPendingReason"
      class="selection-pending-hint"
    >
      已勾选，待配置：{{ selectionPendingReason }}
    </div>

    <div class="range-summary-panel">
      <div class="range-summary-heading">
        <div>
          <div class="range-summary-title">
            识别到 {{ activeRegions.length }} 个数据区域 · 有效
            {{ effectiveRowCount }} 行 · 忽略 {{ ignoredRowCount }} 行
          </div>
          <div class="range-summary-subtitle">
            确认后将保存模板，并自动学习尚未录入的列名；范围按{{
              isExcelFile ? "工作表坐标" : "表格行列"
            }}显示
          </div>
        </div>
        <div class="range-summary-tools">
          <div class="region-header-ranges">
            <el-tag
              v-for="region in regionSummaryItems"
              :key="region.id"
              size="small"
              type="info"
              effect="plain"
            >
              {{ region.label }}表头 {{ region.headerRange }}
            </el-tag>
          </div>
          <el-button
            v-if="!readonly"
            type="primary"
            plain
            size="small"
            :disabled="controlsLocked"
            :aria-label="'调整 ' + tableTitle + ' 的识别范围'"
            @click="rangeEditorVisible = true"
          >
            调整范围
          </el-button>
        </div>
      </div>
      <div class="range-grid">
        <div
          v-for="field in rangeSummaryFields"
          :key="field.label"
          class="range-row"
        >
          <span class="range-label">{{ field.label }}</span>
          <div class="range-values">
            <code v-for="range in field.ranges" :key="range">{{ range }}</code>
            <span v-if="field.ranges.length === 0" class="range-empty">{{
              field.emptyText
            }}</span>
          </div>
        </div>
      </div>
    </div>

    <el-alert
      v-if="structureValidationError"
      type="warning"
      :closable="false"
      show-icon
      :title="structureValidationError"
      class="structure-validation-alert"
    />

    <div v-if="table.skipReason || visibleIssues.length > 0" class="issue-list">
      <el-tag
        v-for="issue in visibleIssues"
        :key="`${issue.code}-${issue.field || ''}-${issue.message}`"
        size="small"
        effect="plain"
        :type="getSmartStructureIssueTagType(issue.severity)"
      >
        {{ issue.message }}
      </el-tag>
      <el-tag
        v-if="table.skipReason && visibleIssues.length === 0"
        size="small"
        type="info"
        effect="plain"
      >
        {{ table.skipReason }}
      </el-tag>
    </div>

    <div v-if="showRecognitionEvidence" class="headers-preview">
      <span class="headers-label">表头</span>
      <el-tag
        v-for="(header, index) in displayHeaders.slice(0, 10)"
        :key="`${table.tableIndex}-${index}`"
        size="small"
        type="info"
        effect="plain"
      >
        [{{ formatColumnCoordinate(index) }}]
        {{ header || `列${index + 1}` }}
      </el-tag>
      <span v-if="displayHeaders.length > 10" class="more">...</span>
    </div>

    <div
      v-if="semanticRecallSuggestions.length > 0"
      class="semantic-recall-list"
    >
      <span class="semantic-recall-label">语义召回建议</span>
      <el-tag
        v-for="suggestion in semanticRecallSuggestions"
        :key="`${suggestion.source}-${suggestion.columnIndex}-${suggestion.targetField}`"
        size="small"
        type="warning"
        effect="plain"
      >
        [{{ formatColumnCoordinate(suggestion.columnIndex) }}]
        {{ suggestion.header || `列${suggestion.columnIndex + 1}` }}
        -> {{ getSmartStructureFieldLabel(suggestion.targetField) }}
        {{ formatSmartStructurePercent(suggestion.confidence) }}
        · {{ suggestion.source || "SemanticRecall" }}
        <template v-if="suggestion.reason">
          · {{ suggestion.reason }}
        </template>
      </el-tag>
    </div>

    <div
      v-if="showRecognitionEvidence && table.fields?.length > 0"
      class="field-list"
    >
      <el-tag
        v-for="field in table.fields"
        :key="`${field.field}-${field.columnIndex}`"
        size="small"
        effect="plain"
      >
        {{ getSmartStructureFieldLabel(field.field) }}:
        {{ field.header || "-" }}
        {{ formatSmartStructurePercent(field.confidence) }}
      </el-tag>
    </div>

    <div v-if="showAdvancedFallback || showConfirmAction" class="card-actions">
      <el-button
        v-if="showAdvancedFallback"
        type="primary"
        link
        :disabled="controlsLocked"
        @click="emit('advanced', table)"
      >
        手动处理
      </el-button>
      <el-button
        v-if="showConfirmAction"
        type="primary"
        :disabled="!canConfirm"
        :loading="confirming"
        :title="confirmDisabledReason || undefined"
        :aria-describedby="
          confirmDisabledReason ? confirmDisabledReasonId : undefined
        "
        @click="emitConfirm"
      >
        {{ confirmActionLabel }}
      </el-button>
      <span
        v-if="showConfirmAction && confirmDisabledReason"
        :id="confirmDisabledReasonId"
        class="sr-only"
      >
        {{ confirmDisabledReason }}
      </span>
    </div>

    <SmartStructureRangeEditorDrawer
      v-model="rangeEditorVisible"
      :table="table"
      :table-info="tableInfo"
      :file-id="fileId"
      :is-excel-file="isExcelFile"
      :regions="activeRegions"
      @save="handleRangesSave"
    />
  </section>
</template>

<style scoped>
.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  white-space: nowrap;
  border: 0;
  clip: rect(0, 0, 0, 0);
}

.smart-structure-card {
  box-sizing: border-box;
  width: 100%;
  min-width: 0;
  max-width: 100%;
  padding: 12px 14px;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.card-header {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 8px;
}

.card-title {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  font-weight: 700;
  color: var(--app-text-primary);
}

.card-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 12px;
  justify-content: flex-end;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.selection-disabled-hint,
.selection-pending-hint {
  padding: 7px 10px;
  margin-bottom: 8px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--app-warning);
  background: var(--app-warning-bg);
  border-radius: 6px;
}

.range-summary-panel {
  padding: 12px;
  margin: 10px 0;
  background: color-mix(in srgb, var(--app-info-bg) 72%, transparent);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.range-summary-heading {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  justify-content: space-between;
  padding-bottom: 10px;
  margin-bottom: 10px;
  border-bottom: 1px solid var(--app-border);
}

.range-summary-title {
  font-size: 14px;
  font-weight: 700;
  color: var(--app-text-primary);
}

.range-summary-subtitle {
  margin-top: 3px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.region-header-ranges,
.range-values {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.range-summary-tools {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: flex-end;
}

.region-header-ranges {
  justify-content: flex-end;
}

.range-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 8px 18px;
}

.range-row {
  display: grid;
  grid-template-columns: 58px minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  min-width: 0;
}

.range-label {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.range-values code {
  padding: 3px 7px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px;
  font-weight: 700;
  color: var(--app-primary);
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 5px;
}

.range-empty {
  font-size: 12px;
  color: var(--app-text-placeholder);
}

.row-range {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.headers-preview,
.field-list,
.semantic-recall-list,
.issue-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  margin-top: 10px;
  margin-bottom: 10px;
}

.headers-label,
.semantic-recall-label,
.more {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.card-actions {
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

@media (width <= 768px) {
  .card-header,
  .card-actions {
    flex-direction: column;
    align-items: stretch;
  }

  .range-summary-heading {
    flex-direction: column;
  }

  .region-header-ranges {
    justify-content: flex-start;
  }

  .range-summary-tools {
    align-items: flex-start;
  }

  .range-grid {
    grid-template-columns: 1fr;
  }

  .card-meta {
    justify-content: flex-start;
  }

  .smart-structure-card {
    padding: 12px;
    overflow-wrap: anywhere;
  }

  .card-title :deep(.el-checkbox),
  .card-actions :deep(.el-button) {
    min-height: 44px;
  }

  .card-actions :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }
}
</style>
