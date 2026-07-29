<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import type { TableInfo } from "@/api/document";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognitionIssue,
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import SmartStructureRangeEditorDrawer from "./SmartStructureRangeEditorDrawer.vue";
import {
  buildSmartStructureHeaderOptions,
  buildSmartConfigConfirmRequest,
  canConfirmSmartStructureTable,
  filterSmartStructureIssuesForRegions,
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
  updateSmartStructureRegionFieldColumn,
  validateSmartStructureRegions
} from "./smart-structure-recognition";
import type { SmartStructureEditableFieldName } from "./smart-structure-recognition";

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

const handleRangesSave = (regions: SmartConfigRecognizedRegion[]) => {
  if (regions.length === 0) return;
  editableRegions.value = regions.map(region => ({
    ...region,
    headers: [...region.headers]
  }));
};

const handleHeaderSelectionChange = (
  regionId: string,
  field: SmartStructureEditableFieldName,
  columnIndex?: number
) => {
  editableRegions.value = editableRegions.value.map(region =>
    region.regionId === regionId
      ? updateSmartStructureRegionFieldColumn(region, field, columnIndex)
      : region
  );
};

const buildColumnRangeSummary = (
  columnIndex: number | null | undefined,
  startRowIndex: number,
  endRowIndex: number | null | undefined
) => {
  if (columnIndex == null) return null;
  const startRow = (props.tableInfo?.usedRangeStartRow ?? 1) + startRowIndex;
  const resolvedEndRowIndex = resolveSmartStructureRegionEndRowIndex(
    { dataStartRowIndex: startRowIndex, dataEndRowIndex: endRowIndex },
    props.tableInfo
  );
  const endRow =
    (props.tableInfo?.usedRangeStartRow ?? 1) + resolvedEndRowIndex;
  return {
    column: formatColumnCoordinate(columnIndex),
    startRow,
    endRow
  };
};

const rangeSummaryFields = computed(() =>
  [
    { label: "项目列", key: "projectColumnIndex", field: "Project" },
    {
      label: "规格列",
      key: "specificationColumnIndex",
      field: "Specification"
    },
    { label: "验收列", key: "acceptanceColumnIndex", field: "Acceptance" },
    { label: "备注列", key: "remarkColumnIndex", field: "Remark" }
  ].map(field => {
    const ranges = activeRegions.value.map((region, regionIndex) => {
      const columnIndex = region[field.key as keyof typeof region] as
        | number
        | null
        | undefined;
      const range = buildColumnRangeSummary(
        columnIndex,
        region.dataStartRowIndex,
        region.dataEndRowIndex
      );
      return {
        key: `${field.key}-${regionIndex}`,
        regionId: region.regionId,
        regionLabel: `区域 ${region.regionIndex + 1}`,
        columnIndex: columnIndex ?? undefined,
        options: buildSmartStructureHeaderOptions(
          region.headers,
          props.tableInfo?.usedRangeStartColumn ?? 1
        ),
        emptyText:
          field.field === "Project" && region.isSpecificationOnly
            ? "仅规格表"
            : "未识别",
        ...range
      };
    });
    return {
      label: field.label,
      field: field.field as SmartStructureEditableFieldName,
      ranges
    };
  })
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

const regionFieldSummaries = computed(() =>
  activeRegions.value.map(region => {
    const regionSummary = regionSummaryItems.value.find(
      item => item.id === region.regionId
    );
    const mappings = [
      {
        field: "Project",
        label: "项目",
        columnIndex: region.isSpecificationOnly
          ? null
          : region.projectColumnIndex
      },
      {
        field: "Specification",
        label: "规格",
        columnIndex: region.specificationColumnIndex
      },
      {
        field: "Acceptance",
        label: "验收",
        columnIndex: region.acceptanceColumnIndex
      },
      {
        field: "Remark",
        label: "备注",
        columnIndex: region.remarkColumnIndex
      }
    ]
      .filter(mapping => mapping.columnIndex != null)
      .map(mapping => {
        const recognizedField = region.fields?.find(
          field => field.field === mapping.field
        );
        return {
          ...mapping,
          header: region.headers[mapping.columnIndex!] || "-",
          confidence: recognizedField?.confidence ?? region.confidence
        };
      });

    return {
      id: region.regionId,
      label: regionSummary?.label ?? `区域 ${region.regionIndex + 1}`,
      headerRange: regionSummary?.headerRange ?? "",
      mappings
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

const isCoveredHeaderIssue = (issue: SmartConfigRecognitionIssue) => {
  if (issue.code !== "UncoveredRegionHeader") return false;
  const absoluteRow = Number(issue.message.match(/第\s*(\d+)\s*行/)?.[1]);
  if (!Number.isInteger(absoluteRow)) return false;
  const rowIndex = absoluteRow - (props.tableInfo?.usedRangeStartRow ?? 1);
  return activeRegions.value.some(
    region =>
      rowIndex === region.dataStartRowIndex - 1 ||
      (rowIndex >= region.headerRowIndex &&
        rowIndex < region.headerRowIndex + Math.max(1, region.headerRowCount))
  );
};

const allIssues = computed(() => {
  const seen = new Set<string>();
  return filterSmartStructureIssuesForRegions(
    [
      ...(props.table.issues ?? []),
      ...activeRegions.value.flatMap(region => region.issues ?? [])
    ],
    activeRegions.value
  ).filter(issue => {
    if (isCoveredHeaderIssue(issue)) return false;
    const key = `${issue.code}-${issue.field ?? ""}-${issue.message}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
});
const structureRecoveryIssueCodes = new Set([
  "TemplateRegionDataChanged",
  "TemplateRegionOutOfRange",
  "TemplateHeaderChanged",
  "TemplateRegionOverlap",
  "TemplateRegionStructureChanged",
  "UnhealthyRegionData",
  "UnassignedDataAfterGap",
  "UncoveredBusinessRows",
  "UncoveredRegionHeader"
]);
const structureRecoveryIssues = computed(() =>
  allIssues.value.filter(issue => structureRecoveryIssueCodes.has(issue.code))
);
const visibleIssues = computed(() =>
  allIssues.value
    .filter(issue => !structureRecoveryIssueCodes.has(issue.code))
    .slice(0, 4)
);
const formatStructureRecoveryDetail = (issue: SmartConfigRecognitionIssue) => {
  if (issue.code === "TemplateRegionDataChanged") {
    return "历史模板范围内未找到足够的有效数据，行列位置可能已经变化";
  }
  if (issue.code === "TemplateRegionOutOfRange") {
    return "历史模板记录的范围已超出当前表格";
  }
  if (issue.code === "TemplateHeaderChanged") {
    return "当前文件表头与历史模板不一致，列位置可能已经变化";
  }
  if (issue.code === "TemplateRegionOverlap") {
    return "历史模板中的数据区域在当前文件中发生重叠";
  }
  if (issue.code === "UnhealthyRegionData") {
    return "当前范围内的有效数据不足，可能需要重新选择数据行";
  }
  if (issue.code === "UnassignedDataAfterGap") {
    return "空白行之后仍检测到疑似业务数据，可能需要扩大数据范围";
  }
  if (issue.code === "UncoveredRegionHeader") {
    const row = issue.message.match(/第\s*(\d+)\s*行/)?.[1];
    return row
      ? `第 ${row} 行可能是新的表头，当前范围尚未包含该区域`
      : "发现可能的新表头，当前范围尚未包含该区域";
  }
  if (issue.code === "UncoveredBusinessRows") {
    return issue.message
      .replace(
        "存在未被任何区域覆盖的疑似业务数据",
        "的疑似业务数据未包含在当前范围内"
      )
      .replace("请确认范围", "请调整范围");
  }
  return issue.message;
};
const structureRecoveryDetails = computed(() =>
  structureRecoveryIssues.value.map(formatStructureRecoveryDetail)
);
const semanticRecallSuggestions = computed(
  () => props.table.semanticRecallSuggestions?.slice(0, 6) ?? []
);
const showRecognitionEvidence = computed(
  () =>
    props.table.decision !== "AutoApply" ||
    props.table.confidence < 0.8 ||
    allIssues.value.length > 0 ||
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
            v-if="!readonly && structureRecoveryIssues.length === 0"
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
          <div class="range-values">
            <div
              v-for="range in field.ranges"
              :key="range.key"
              class="range-mapping"
            >
              <div class="range-field-heading">
                <span class="range-label">{{ field.label }}</span>
                <template v-if="isExcelFile">
                  <span class="range-header-bracket" aria-hidden="true">[</span>
                  <el-select
                    class="range-header-select"
                    :model-value="range.columnIndex"
                    :disabled="controlsLocked"
                    clearable
                    placeholder="请选择"
                    :aria-label="`${range.regionLabel}${field.label}表头`"
                    @change="
                      value =>
                        handleHeaderSelectionChange(
                          range.regionId,
                          field.field,
                          value as number | undefined
                        )
                    "
                  >
                    <el-option
                      v-for="option in range.options"
                      :key="option.columnIndex"
                      :value="option.columnIndex"
                      :label="option.header"
                    >
                      <span>{{ option.header }}</span>
                      <span class="range-header-option-column">
                        {{ option.columnLabel }}列
                      </span>
                    </el-option>
                  </el-select>
                  <span class="range-header-bracket" aria-hidden="true">]</span>
                </template>
              </div>
              <div
                v-if="range.column"
                class="range-interval"
                :aria-label="`${range.column}${range.startRow} 到 ${range.column}${range.endRow}`"
              >
                <span class="range-interval-value"
                  >{{ range.column }}{{ range.startRow }}</span
                >
                <span class="range-interval-line" aria-hidden="true" />
                <span class="range-interval-value"
                  >{{ range.column }}{{ range.endRow }}</span
                >
              </div>
              <span v-else class="range-empty">{{ range.emptyText }}</span>
            </div>
          </div>
        </div>
      </div>
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

    <el-alert
      v-if="structureValidationError"
      type="warning"
      :closable="false"
      show-icon
      :title="structureValidationError"
      class="structure-validation-alert"
    />

    <section
      v-if="structureRecoveryIssues.length > 0"
      class="structure-recovery-alert"
      role="alert"
      aria-live="polite"
    >
      <div class="structure-recovery-alert__content">
        <strong>文件结构与历史模板不一致，需要重新确认</strong>
        <span>
          当前文件的表头或数据位置可能发生变化。请调整范围，确认表头、数据行以及项目、规格、验收、备注列。
        </span>
        <ul class="structure-recovery-alert__details">
          <li v-for="detail in structureRecoveryDetails" :key="detail">
            {{ detail }}
          </li>
        </ul>
      </div>
      <el-button
        v-if="!readonly"
        type="primary"
        :disabled="controlsLocked"
        :aria-label="'调整 ' + tableTitle + ' 的识别范围'"
        @click="rangeEditorVisible = true"
      >
        调整范围
      </el-button>
    </section>

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

    <div v-if="showRecognitionEvidence" class="region-field-list">
      <div
        v-for="region in regionFieldSummaries"
        :key="region.id"
        class="region-field-row"
      >
        <span class="region-field-label">
          {{ region.label }}表头 {{ region.headerRange }}
        </span>
        <div class="field-list">
          <el-tag
            v-for="mapping in region.mappings"
            :key="`${region.id}-${mapping.field}-${mapping.columnIndex}`"
            size="small"
            effect="plain"
          >
            {{ mapping.label }}: {{ mapping.header }}
            {{ formatSmartStructurePercent(mapping.confidence) }}
          </el-tag>
        </div>
      </div>
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

.region-header-ranges {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  justify-content: flex-end;
}

.range-summary-tools {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: flex-end;
}

.range-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 8px 18px;
}

.range-row {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: flex-start;
  min-width: 0;
}

.range-mapping {
  display: grid;
  gap: 7px;
  justify-items: start;
  min-width: 0;
}

.range-field-heading {
  display: flex;
  align-items: center;
  min-width: 0;
  max-width: 100%;
  white-space: nowrap;
}

.range-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.range-header-bracket {
  font-size: 12px;
  color: var(--app-text-primary);
}

.range-header-select {
  width: clamp(88px, 10vw, 168px);
  min-width: 0;
}

.range-header-select :deep(.el-select__wrapper) {
  min-height: 24px;
  padding: 0 4px;
  background: transparent;
  box-shadow: none;
}

.range-header-select :deep(.el-select__selected-item) {
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.range-header-option-column {
  float: right;
  margin-left: 18px;
  color: var(--app-text-secondary);
}

.range-values {
  display: grid;
  gap: 14px;
}

.range-interval {
  display: grid;
  justify-items: center;
  min-width: 28px;
}

.range-interval-value {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12px;
  font-weight: 700;
  color: var(--app-primary);
}

.range-interval-line {
  width: 1px;
  height: 18px;
  margin: 3px 0;
  background: var(--app-text-secondary);
}

.range-empty {
  font-size: 12px;
  color: var(--app-text-placeholder);
}

.row-range {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.structure-recovery-alert {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
  padding: 11px 12px;
  margin: 10px 0;
  color: var(--app-text-primary);
  background: var(--app-warning-bg);
  border: 1px solid color-mix(in srgb, var(--app-warning) 35%, transparent);
  border-radius: 8px;
}

.structure-recovery-alert__content {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
  font-size: 12px;
  line-height: 1.6;
}

.structure-recovery-alert__content strong {
  font-size: 13px;
  color: var(--app-warning);
}

.structure-recovery-alert__details {
  padding-left: 18px;
  margin: 2px 0 0;
  color: var(--app-text-secondary);
}

.structure-recovery-alert :deep(.el-button) {
  flex: none;
  min-height: 44px;
}

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

.semantic-recall-label,
.more {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.region-field-list {
  display: grid;
  gap: 8px;
  margin-top: 10px;
}

.region-field-row {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}

.region-field-label {
  flex: 0 0 auto;
  min-width: 132px;
  padding-top: 3px;
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary);
}

.region-field-row .field-list {
  margin: 0;
}

.card-actions {
  display: flex;
  gap: 10px;
  justify-content: flex-end;
}

@media (width <= 768px) {
  .card-header,
  .card-actions,
  .structure-recovery-alert {
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

  .region-field-row {
    flex-direction: column;
    gap: 4px;
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

  .structure-recovery-alert :deep(.el-button) {
    width: 100%;
    margin-left: 0;
  }
}
</style>
