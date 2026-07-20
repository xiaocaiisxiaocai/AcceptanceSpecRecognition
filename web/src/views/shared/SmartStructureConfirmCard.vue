<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { getTablePreview, type TableInfo } from "@/api/document";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import SmartStructureRangeEditorDrawer from "./SmartStructureRangeEditorDrawer.vue";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import {
  buildSmartConfigConfirmRequest,
  canConfirmSmartStructureTable,
  formatSmartStructurePercent,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel,
  getSmartStructureIssueTagType,
  getSmartStructureRecommendationTag,
  getSmartStructureTableKindLabel,
  needsManualStructureFallback,
  countSmartStructureRegionRows,
  resolveSmartStructureRegionEndRowIndex,
  validateSmartStructureRegions
} from "./smart-structure-recognition";
import { requiredSelectionRule, validateForm } from "@/utils/form-rules";

const props = withDefaults(
  defineProps<{
    table: SmartConfigRecognizedTable;
    tableInfo?: TableInfo;
    fileId?: number;
    customerId?: number;
    confirming?: boolean;
    confirmationLocked?: boolean;
    readonly?: boolean;
    defaultExpanded?: boolean;
    importSelected?: boolean;
    importSelectable?: boolean;
    selectionDisabledReason?: string;
    selectionPendingReason?: string;
    isExcelFile?: boolean;
  }>(),
  {
    confirmationLocked: false,
    isExcelFile: true
  }
);

const emit = defineEmits<{
  confirm: [request: SmartConfigConfirmRequest];
  advanced: [table: SmartConfigRecognizedTable];
  "update:importSelected": [value: boolean];
}>();

type EditableState = {
  templateName: string;
  projectColumnIndex?: number;
  specificationColumnIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex?: number;
  isSpecificationOnly: boolean;
};

const state = reactive<EditableState>({
  templateName: "",
  projectColumnIndex: undefined,
  specificationColumnIndex: undefined,
  acceptanceColumnIndex: undefined,
  remarkColumnIndex: undefined,
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  dataEndRowIndex: undefined,
  isSpecificationOnly: false
});
const formRef = ref<FormInstance>();
const formRules: FormRules<EditableState> = {
  projectColumnIndex: [
    {
      validator: (_rule, value, callback) => {
        if (state.isSpecificationOnly || value != null) callback();
        else callback(new Error("请选择项目列"));
      },
      trigger: "change"
    }
  ],
  specificationColumnIndex: [requiredSelectionRule("请选择规格列")],
  acceptanceColumnIndex: [requiredSelectionRule("请选择验收列")]
};
const detailVisible = ref(false);
const rangeEditorVisible = ref(false);
const editableRegions = ref<SmartConfigRecognizedRegion[]>([]);
const currentHeaders = ref<string[]>([]);
const headersLoading = ref(false);
let latestHeaderRequestId = 0;
let resettingState = false;
const controlsLocked = computed(() =>
  Boolean(props.readonly || props.confirmationLocked)
);

const resetState = () => {
  resettingState = true;
  latestHeaderRequestId += 1;
  headersLoading.value = false;
  currentHeaders.value = [...props.table.headers];
  state.templateName =
    props.table.tableName?.trim() || `表格 ${props.table.tableIndex + 1}`;
  state.projectColumnIndex = props.table.projectColumnIndex ?? undefined;
  state.specificationColumnIndex =
    props.table.specificationColumnIndex ?? undefined;
  state.acceptanceColumnIndex = props.table.acceptanceColumnIndex ?? undefined;
  state.remarkColumnIndex = props.table.remarkColumnIndex ?? undefined;
  state.headerRowIndex = props.table.headerRowIndex;
  state.headerRowCount = props.table.headerRowCount;
  state.dataStartRowIndex = props.table.dataStartRowIndex;
  state.dataEndRowIndex = props.table.dataEndRowIndex ?? undefined;
  state.isSpecificationOnly = props.table.isSpecificationOnly;
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
  detailVisible.value = props.defaultExpanded ?? false;
  rangeEditorVisible.value = false;
  resettingState = false;
};

watch(() => props.table, resetState, { immediate: true });
watch(
  () => props.confirmationLocked,
  locked => {
    if (locked) rangeEditorVisible.value = false;
  }
);

watch(
  () => state.isSpecificationOnly,
  isSpecificationOnly => {
    if (isSpecificationOnly) {
      state.projectColumnIndex = undefined;
    }
  }
);

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

const columnOptions = computed(() =>
  currentHeaders.value.map((header, index) => {
    return {
      value: index,
      label: `[${formatColumnCoordinate(index)}] ${header || `列${index + 1}`}`
    };
  })
);

const activeRegions = computed(() => {
  const regions = editableRegions.value;
  return regions.map((region, index) =>
    index === 0
      ? {
          ...region,
          headers: currentHeaders.value,
          headerRowIndex: state.headerRowIndex,
          headerRowCount: state.headerRowCount,
          dataStartRowIndex: state.dataStartRowIndex,
          dataEndRowIndex: state.dataEndRowIndex,
          projectColumnIndex: state.projectColumnIndex,
          specificationColumnIndex: state.specificationColumnIndex,
          acceptanceColumnIndex: state.acceptanceColumnIndex,
          remarkColumnIndex: state.remarkColumnIndex,
          isSpecificationOnly: state.isSpecificationOnly
        }
      : region
  );
});

const handleRangesSave = (regions: SmartConfigRecognizedRegion[]) => {
  if (regions.length === 0) return;
  editableRegions.value = regions.map(region => ({
    ...region,
    headers: [...region.headers]
  }));

  const primary = editableRegions.value[0];
  resettingState = true;
  currentHeaders.value = [...primary.headers];
  state.projectColumnIndex = primary.projectColumnIndex ?? undefined;
  state.specificationColumnIndex =
    primary.specificationColumnIndex ?? undefined;
  state.acceptanceColumnIndex = primary.acceptanceColumnIndex ?? undefined;
  state.remarkColumnIndex = primary.remarkColumnIndex ?? undefined;
  state.headerRowIndex = primary.headerRowIndex;
  state.headerRowCount = primary.headerRowCount;
  state.dataStartRowIndex = primary.dataStartRowIndex;
  state.dataEndRowIndex = primary.dataEndRowIndex ?? undefined;
  state.isSpecificationOnly = primary.isSpecificationOnly;
  resettingState = false;
  void loadHeadersForCurrentStructure();
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
      .filter(range => range !== "-")
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

const hasRequiredProjectColumn = computed(
  () => state.isSpecificationOnly || state.projectColumnIndex != null
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

const visibleIssues = computed(() => props.table.issues?.slice(0, 4) ?? []);
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
    confirmationLocked: props.confirmationLocked,
    customerId: props.customerId,
    allRegionsConfirmable: allRegionsConfirmable.value,
    structureValidationError: structureValidationError.value,
    decision: props.table.decision,
    hasStructureChanges: hasStructureChanges.value
  })
);

const confirmDisabledReason = computed(() => {
  if (canConfirm.value || props.readonly || props.confirmationLocked) return "";
  if (props.table.decision === "Reject" && !hasStructureChanges.value) {
    return "识别结果不可用，请先调整范围或列映射后再确认";
  }
  return structureValidationError.value || "请先补齐必填字段后再确认";
});
const confirmDisabledReasonId = computed(
  () => `smart-structure-confirm-reason-${props.table.tableIndex}`
);

const displayHeaderRowIndex = computed({
  get: () => (props.tableInfo?.usedRangeStartRow ?? 1) + state.headerRowIndex,
  set: value => {
    state.headerRowIndex = Math.max(
      0,
      (value ?? props.tableInfo?.usedRangeStartRow ?? 1) -
        (props.tableInfo?.usedRangeStartRow ?? 1)
    );
  }
});

const minimumDataStartRowIndex = computed(
  () => state.headerRowIndex + Math.max(state.headerRowCount, 1)
);
const displayMinimumDataStartRowIndex = computed(
  () =>
    (props.tableInfo?.usedRangeStartRow ?? 1) + minimumDataStartRowIndex.value
);
const displayMaximumRow = computed(
  () =>
    (props.tableInfo?.usedRangeStartRow ?? 1) +
    Math.max(0, (props.tableInfo?.rowCount ?? 1) - 1)
);

const normalizeHeaders = (headers: string[], columnCount: number) =>
  Array.from(
    { length: Math.max(headers.length, columnCount) },
    (_, index) => headers[index] ?? ""
  );

const loadHeadersForCurrentStructure = async () => {
  if (!props.fileId) return;

  const requestId = ++latestHeaderRequestId;
  headersLoading.value = true;
  try {
    const res = await getTablePreview(props.fileId, props.table.tableIndex, {
      previewRows: 1,
      headerRowIndex: state.headerRowIndex,
      headerRowCount: state.headerRowCount,
      dataStartRowIndex: minimumDataStartRowIndex.value
    });
    if (requestId !== latestHeaderRequestId) return;

    if (res.code !== 0) {
      throw new Error(res.message || "加载表头失败");
    }

    currentHeaders.value = normalizeHeaders(
      res.data.headers,
      res.data.columnCount
    );
  } catch (error) {
    if (
      requestId === latestHeaderRequestId &&
      !isGloballyHandledAuthError(error)
    ) {
      ElMessage.error(getRequestErrorMessage(error, "加载表头失败"));
    }
  } finally {
    if (requestId === latestHeaderRequestId) {
      headersLoading.value = false;
    }
  }
};

watch(
  () => [state.headerRowIndex, state.headerRowCount] as const,
  () => {
    if (!resettingState) {
      void loadHeadersForCurrentStructure();
    }
  },
  { flush: "sync" }
);

watch(minimumDataStartRowIndex, minimum => {
  if (state.dataStartRowIndex < minimum) {
    state.dataStartRowIndex = minimum;
  }
});

const displayDataStartRowIndex = computed({
  get: () =>
    (props.tableInfo?.usedRangeStartRow ?? 1) + state.dataStartRowIndex,
  set: value => {
    state.dataStartRowIndex = Math.max(
      (value ?? props.tableInfo?.usedRangeStartRow ?? 1) -
        (props.tableInfo?.usedRangeStartRow ?? 1),
      minimumDataStartRowIndex.value
    );
  }
});

const emitConfirm = async () => {
  if (!(await validateForm(formRef.value))) return;

  if (structureValidationError.value) {
    ElMessage.warning(structureValidationError.value);
    return;
  }

  if (
    !props.customerId ||
    !hasRequiredProjectColumn.value ||
    state.specificationColumnIndex == null ||
    state.acceptanceColumnIndex == null
  ) {
    return;
  }

  emit(
    "confirm",
    buildSmartConfigConfirmRequest(
      props.customerId,
      {
        ...props.table,
        headers: [...currentHeaders.value],
        tableName: state.templateName,
        projectColumnIndex: state.projectColumnIndex,
        specificationColumnIndex: state.specificationColumnIndex,
        acceptanceColumnIndex: state.acceptanceColumnIndex,
        remarkColumnIndex: state.remarkColumnIndex,
        headerRowIndex: state.headerRowIndex,
        headerRowCount: state.headerRowCount,
        dataStartRowIndex: state.dataStartRowIndex,
        dataEndRowIndex: state.dataEndRowIndex,
        isSpecificationOnly: state.isSpecificationOnly,
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
        <span>{{ table.source || "-" }}</span>
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
            <span v-if="field.ranges.length === 0" class="range-empty"
              >未识别</span
            >
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

    <div
      v-if="showRecognitionEvidence"
      v-show="detailVisible"
      class="headers-preview"
    >
      <span class="headers-label">表头</span>
      <el-tag
        v-for="(header, index) in currentHeaders.slice(0, 10)"
        :key="`${table.tableIndex}-${index}`"
        size="small"
        type="info"
        effect="plain"
      >
        [{{ formatColumnCoordinate(index) }}]
        {{ header || `列${index + 1}` }}
      </el-tag>
      <span v-if="currentHeaders.length > 10" class="more">...</span>
    </div>

    <div
      v-show="detailVisible"
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

    <el-form
      v-show="detailVisible"
      ref="formRef"
      :model="state"
      :rules="formRules"
      label-width="96px"
      size="small"
      class="confirm-form"
      status-icon
    >
      <el-row :gutter="14">
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="模板名">
            <el-input
              v-model="state.templateName"
              :disabled="controlsLocked"
              placeholder="确认后保存为客户模板"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="项目列" prop="projectColumnIndex">
            <el-select
              v-model="state.projectColumnIndex"
              :disabled="
                controlsLocked || state.isSpecificationOnly || headersLoading
              "
              :loading="headersLoading"
              placeholder="请选择项目列"
              clearable
              style="width: 100%"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="规格列" prop="specificationColumnIndex">
            <el-select
              v-model="state.specificationColumnIndex"
              :disabled="controlsLocked || headersLoading"
              :loading="headersLoading"
              placeholder="请选择规格列"
              clearable
              style="width: 100%"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="验收列" prop="acceptanceColumnIndex">
            <el-select
              v-model="state.acceptanceColumnIndex"
              :disabled="controlsLocked || headersLoading"
              :loading="headersLoading"
              placeholder="请选择验收列"
              clearable
              style="width: 100%"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="备注列">
            <el-select
              v-model="state.remarkColumnIndex"
              :disabled="controlsLocked || headersLoading"
              :loading="headersLoading"
              placeholder="请选择备注列（可选）"
              clearable
              style="width: 100%"
            >
              <el-option
                v-for="opt in columnOptions"
                :key="opt.value"
                :label="opt.label"
                :value="opt.value"
              />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="仅规格">
            <el-switch
              v-model="state.isSpecificationOnly"
              :disabled="controlsLocked"
              :aria-label="tableTitle + ' 是否仅规格表'"
              active-text="是"
              inactive-text="否"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="表头行">
            <el-input-number
              v-model="displayHeaderRowIndex"
              :disabled="controlsLocked"
              :min="tableInfo?.usedRangeStartRow ?? 1"
              :max="tableInfo ? displayMaximumRow : undefined"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="表头行数">
            <el-input-number
              v-model="state.headerRowCount"
              :disabled="controlsLocked"
              :min="1"
              :max="
                tableInfo
                  ? Math.max(1, displayMaximumRow - displayHeaderRowIndex + 1)
                  : undefined
              "
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="数据起始">
            <el-input-number
              v-model="displayDataStartRowIndex"
              :disabled="controlsLocked"
              :min="displayMinimumDataStartRowIndex"
              :max="tableInfo ? displayMaximumRow : undefined"
            />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <div
      v-show="detailVisible"
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

    <div class="card-actions">
      <el-button type="primary" link @click="detailVisible = !detailVisible">
        {{ detailVisible ? "收起高级设置" : "高级设置" }}
      </el-button>
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
        type="primary"
        :disabled="!canConfirm || headersLoading"
        :loading="confirming"
        :title="confirmDisabledReason || undefined"
        :aria-describedby="
          confirmDisabledReason ? confirmDisabledReasonId : undefined
        "
        @click="emitConfirm"
      >
        确认并学习
      </el-button>
      <span
        v-if="confirmDisabledReason"
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

.confirm-form {
  padding: 10px 12px 0;
  margin-top: 10px;
  margin-bottom: 10px;
  background: var(--app-info-bg);
  border: 1px solid var(--app-border);
  border-radius: 8px;
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

  .confirm-form {
    padding: 10px 8px 0;
  }

  .confirm-form :deep(.el-form-item__content),
  .confirm-form :deep(.el-input__wrapper),
  .confirm-form :deep(.el-select__wrapper),
  .confirm-form :deep(.el-input-number) {
    min-height: 44px;
  }

  .confirm-form :deep(.el-input-number) {
    width: 100%;
  }
}
</style>
