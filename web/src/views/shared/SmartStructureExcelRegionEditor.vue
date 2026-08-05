<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { getTablePreview, type TableInfo } from "@/api/document";
import type {
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  applySmartStructureExcelColumnPatch,
  applySmartStructureExcelEndpointPatch,
  applySmartStructureExcelRowPatch,
  createSmartStructureExcelRegionDraft,
  formatSmartStructureExcelFieldEndpoints,
  getSmartStructureExcelRowInputLimits,
  resolveSmartStructureExcelBlockingValidationError,
  setSmartStructureSpecificationOnly,
  toSmartConfigRecognizedRegion,
  validateSmartStructureExcelRegionDrafts,
  type SmartStructureExcelEndpoint,
  type SmartStructureExcelField,
  type SmartStructureExcelFieldEndpoints,
  type SmartStructureExcelRegionBounds,
  type SmartStructureExcelRegionDraft,
  type SmartStructureExcelRegionValidationField,
  type SmartStructureExcelRowPatch
} from "./smart-structure-region-draft";
import { createSmartStructureHeaderPreviewLoader } from "./smart-structure-region-header-preview";
import { toExcelColumnLabel } from "./smart-structure-recognition";

const props = defineProps<{
  modelValue: SmartConfigRecognizedRegion[];
  table: SmartConfigRecognizedTable;
  tableInfo?: TableInfo;
  fileId?: number;
  disabled?: boolean;
  resetVersion?: number;
  compact?: boolean;
}>();

const emit = defineEmits<{
  "update:modelValue": [regions: SmartConfigRecognizedRegion[]];
  "validation-change": [error: string];
  "user-edit": [];
  reset: [];
}>();

const fieldDefinitions: Array<{
  field: SmartStructureExcelField;
  label: string;
  columnField: SmartStructureExcelRegionValidationField;
  required: boolean;
}> = [
  {
    field: "project",
    label: "项目",
    columnField: "projectColumn",
    required: true
  },
  {
    field: "specification",
    label: "规格",
    columnField: "specificationColumn",
    required: true
  },
  {
    field: "acceptance",
    label: "验收",
    columnField: "acceptanceColumn",
    required: true
  },
  {
    field: "remark",
    label: "备注",
    columnField: "remarkColumn",
    required: true
  }
];

const getA1EndpointPlaceholder = (
  definition: (typeof fieldDefinitions)[number],
  endpoint: SmartStructureExcelEndpoint
) =>
  `${definition.label}${endpoint === "start" ? "起始" : "结束"}，例如 ${
    endpoint === "start" ? "B2" : "B4"
  }`;

const drafts = ref<SmartStructureExcelRegionDraft[]>([]);
const a1EndpointBuffers = reactive<
  Record<
    string,
    Partial<Record<SmartStructureExcelField, SmartStructureExcelFieldEndpoints>>
  >
>({});
const a1Errors = reactive<
  Record<
    string,
    Partial<
      Record<
        SmartStructureExcelField,
        Partial<Record<SmartStructureExcelEndpoint, string>>
      >
    >
  >
>({});
const headerErrors = reactive<Record<string, string>>({});
const headerLoading = reactive<Record<string, boolean>>({});
const startRowValues = reactive<Record<string, string[]>>({});
const endRowValues = reactive<Record<string, string[]>>({});
const synchronizationNotices = reactive<Record<string, string>>({});
const headerLoadTimers = new Map<string, ReturnType<typeof setTimeout>>();
const detailsExpanded = ref(false);
const selectedCompactRegionId = ref<string | null>(null);
let manualRegionSequence = 0;

const detailsToolbarId = computed(
  () => `excel-region-details-toolbar-${props.table.tableIndex}`
);
const getRegionDetailsId = (regionIndex: number) =>
  `excel-region-details-region-${props.table.tableIndex}-${regionIndex}`;
const detailsControlIds = computed(() =>
  [
    detailsToolbarId.value,
    ...drafts.value.map((_, regionIndex) => getRegionDetailsId(regionIndex))
  ].join(" ")
);
const selectedCompactRegionIndex = computed(() =>
  drafts.value.findIndex(
    draft => draft.regionId === selectedCompactRegionId.value
  )
);
const selectedCompactDraft = computed(() =>
  selectedCompactRegionIndex.value >= 0
    ? drafts.value[selectedCompactRegionIndex.value]
    : undefined
);

const bounds = computed<SmartStructureExcelRegionBounds>(() => {
  const baseRow = props.tableInfo?.usedRangeStartRow ?? 1;
  const baseColumn = props.tableInfo?.usedRangeStartColumn ?? 1;
  const inferredRowCount = Math.max(
    1,
    ...props.modelValue.map(region =>
      region.dataEndRowIndex == null
        ? region.dataStartRowIndex + 1
        : region.dataEndRowIndex + 1
    )
  );
  const inferredColumnCount = Math.max(
    1,
    props.table.headers.length,
    props.tableInfo?.headers.length ?? 0,
    ...props.modelValue.map(region => region.headers.length)
  );
  return {
    baseRow,
    baseColumn,
    rowCount:
      props.tableInfo?.rowCount && props.tableInfo.rowCount > 0
        ? props.tableInfo.rowCount
        : inferredRowCount,
    columnCount: Math.max(
      1,
      props.tableInfo?.columnCount && props.tableInfo.columnCount > 0
        ? props.tableInfo.columnCount
        : inferredColumnCount
    )
  };
});

const maximumRow = computed(
  () => bounds.value.baseRow + bounds.value.rowCount - 1
);
const rowInputLimits = (draft: SmartStructureExcelRegionDraft) =>
  getSmartStructureExcelRowInputLimits(draft, bounds.value);

const headerPreviewLoader = createSmartStructureHeaderPreviewLoader(
  async (fileId, tableIndex, options) =>
    getTablePreview(fileId, tableIndex, options)
);

const clearRecord = (record: Record<string, unknown>) => {
  Object.keys(record).forEach(key => delete record[key]);
};

const refreshA1EndpointBuffers = (
  regionDrafts: SmartStructureExcelRegionDraft[]
) => {
  clearRecord(a1EndpointBuffers);
  clearRecord(a1Errors);
  for (const draft of regionDrafts) {
    a1EndpointBuffers[draft.regionId] = Object.fromEntries(
      fieldDefinitions.map(definition => [
        definition.field,
        formatSmartStructureExcelFieldEndpoints(draft, definition.field)
      ])
    );
    a1Errors[draft.regionId] = {};
  }
};

const resetFromModel = () => {
  selectedCompactRegionId.value = null;
  headerLoadTimers.forEach(timer => clearTimeout(timer));
  headerLoadTimers.clear();
  new Set([
    ...drafts.value.map(draft => draft.regionId),
    ...props.modelValue.map(region => region.regionId)
  ]).forEach(regionId => headerPreviewLoader.invalidate(regionId));
  clearRecord(headerErrors);
  clearRecord(headerLoading);
  clearRecord(startRowValues);
  clearRecord(endRowValues);
  clearRecord(synchronizationNotices);
  drafts.value = props.modelValue.map(region =>
    createSmartStructureExcelRegionDraft(region, bounds.value)
  );
  refreshA1EndpointBuffers(drafts.value);
  drafts.value.forEach(draft => scheduleHeaderLoad(draft.regionId));
};

const serializeModel = (regions: SmartConfigRecognizedRegion[]) =>
  JSON.stringify({ bounds: bounds.value, regions });
const modelToken = computed(() => serializeModel(props.modelValue));
let lastEmittedModelToken = "";

const validationIssues = computed(() =>
  validateSmartStructureExcelRegionDrafts(drafts.value, bounds.value)
);

const firstValidationError = computed(() =>
  resolveSmartStructureExcelBlockingValidationError(
    drafts.value,
    a1Errors,
    bounds.value
  )
);

watch(firstValidationError, error => emit("validation-change", error), {
  immediate: true
});

const emitRegions = (userEdit = true) => {
  const regions = drafts.value.map((draft, index) =>
    toSmartConfigRecognizedRegion(draft, bounds.value, index)
  );
  lastEmittedModelToken = serializeModel(regions);
  emit("update:modelValue", regions);
  if (userEdit) emit("user-edit");
};

const replaceDraft = (
  index: number,
  next: SmartStructureExcelRegionDraft,
  userEdit = true
) => {
  drafts.value = drafts.value.map((draft, draftIndex) =>
    draftIndex === index ? next : draft
  );
  const currentBuffers = a1EndpointBuffers[next.regionId] ?? {};
  const currentErrors = a1Errors[next.regionId] ?? {};
  a1EndpointBuffers[next.regionId] = Object.fromEntries(
    fieldDefinitions.map(definition => {
      const formatted = formatSmartStructureExcelFieldEndpoints(
        next,
        definition.field
      );
      const previous = currentBuffers[definition.field];
      const fieldErrors = currentErrors[definition.field];
      return [
        definition.field,
        {
          start: fieldErrors?.start ? (previous?.start ?? "") : formatted.start,
          end: fieldErrors?.end ? (previous?.end ?? "") : formatted.end
        }
      ];
    })
  );
  a1Errors[next.regionId] = currentErrors;
  emitRegions(userEdit);
};

const columnOptions = (draft: SmartStructureExcelRegionDraft) =>
  Array.from({ length: bounds.value.columnCount }, (_, relativeIndex) => {
    const absoluteColumn = bounds.value.baseColumn + relativeIndex;
    return {
      value: absoluteColumn,
      column: toExcelColumnLabel(absoluteColumn),
      label: draft.source.headers[relativeIndex] || `第 ${absoluteColumn} 列`
    };
  });

const getColumnValue = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
) => {
  switch (field) {
    case "project":
      return draft.projectColumn;
    case "specification":
      return draft.specificationColumn;
    case "acceptance":
      return draft.acceptanceColumn;
    case "remark":
      return draft.remarkColumn;
  }
};

const getStartCellContentId = (
  regionIndex: number,
  field: SmartStructureExcelField
) =>
  `excel-region-start-cell-content-${props.table.tableIndex}-${regionIndex}-${field}`;
const getEndCellContentId = (
  regionIndex: number,
  field: SmartStructureExcelField
) =>
  `excel-region-end-cell-content-${props.table.tableIndex}-${regionIndex}-${field}`;
const getA1EndpointErrorId = (
  regionIndex: number,
  field: SmartStructureExcelField,
  endpoint: SmartStructureExcelEndpoint
) =>
  `excel-region-${endpoint}-cell-error-${props.table.tableIndex}-${regionIndex}-${field}`;

const getEndpointCellContent = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField,
  endpoint: SmartStructureExcelEndpoint
) => {
  if (a1Errors[draft.regionId]?.[field]?.[endpoint]) return "地址无效";
  if (headerLoading[draft.regionId]) return "读取中…";
  if (headerErrors[draft.regionId]) return "读取失败";
  if (!props.fileId) return "暂无预览";

  const column = getColumnValue(draft, field);
  if (column == null) return "未使用";
  const values =
    endpoint === "start"
      ? startRowValues[draft.regionId]
      : endRowValues[draft.regionId];
  const relativeColumn = column - bounds.value.baseColumn;
  if (!values || relativeColumn < 0 || relativeColumn >= values.length) {
    return "暂无内容";
  }
  const value = values[relativeColumn]?.trim();
  return value || "空白单元格";
};
const getStartCellContent = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
) => getEndpointCellContent(draft, field, "start");
const getEndCellContent = (
  draft: SmartStructureExcelRegionDraft,
  field: SmartStructureExcelField
) => getEndpointCellContent(draft, field, "end");

const handleColumnChange = (
  index: number,
  field: SmartStructureExcelField,
  value: number | null | undefined
) => {
  const current = drafts.value[index];
  const currentColumn = getColumnValue(current, field);
  const next = applySmartStructureExcelColumnPatch(current, field, value);
  delete a1Errors[next.regionId]?.[field];
  delete synchronizationNotices[next.regionId];
  replaceDraft(index, next);
  if (getColumnValue(next, field) !== currentColumn) {
    scheduleHeaderLoad(next.regionId);
  }
};

const loadHeaders = async (regionId: string) => {
  const index = drafts.value.findIndex(draft => draft.regionId === regionId);
  if (index < 0) return;
  if (!props.fileId) {
    delete headerLoading[regionId];
    delete headerErrors[regionId];
    delete startRowValues[regionId];
    delete endRowValues[regionId];
    return;
  }
  const draft = drafts.value[index];
  const headerStartRow = draft.dataStartRow - 1;
  if (
    headerStartRow < bounds.value.baseRow ||
    headerStartRow > maximumRow.value
  ) {
    delete headerLoading[regionId];
    return;
  }

  headerLoading[regionId] = true;
  delete headerErrors[regionId];
  const result = await headerPreviewLoader.load({
    regionId,
    fileId: props.fileId,
    tableIndex: props.table.tableIndex,
    baseRow: bounds.value.baseRow,
    dataStartRow: draft.dataStartRow,
    dataEndRow: draft.dataEndRow,
    minimumColumnCount: bounds.value.columnCount,
    startValueColumnIndexes: fieldDefinitions
      .map(definition => getColumnValue(draft, definition.field))
      .filter((column): column is number => column != null)
      .map(column => column - bounds.value.baseColumn)
  });
  if (result.status === "stale") return;
  headerLoading[regionId] = false;
  if (result.status === "error") {
    delete startRowValues[regionId];
    delete endRowValues[regionId];
    headerErrors[regionId] = result.message;
    ElMessage.error(result.message);
    return;
  }

  const currentIndex = drafts.value.findIndex(
    item => item.regionId === regionId
  );
  if (currentIndex < 0) return;
  const current = drafts.value[currentIndex];
  const next = {
    ...current,
    source: {
      ...current.source,
      headers: [...result.headers]
    }
  };
  startRowValues[regionId] = [...result.startRowValues];
  endRowValues[regionId] = [...result.endRowValues];
  if (result.warning) {
    headerErrors[regionId] = result.warning;
    ElMessage.warning(result.warning);
  } else {
    delete headerErrors[regionId];
  }
  replaceDraft(currentIndex, next, false);
};

const scheduleHeaderLoad = (regionId: string) => {
  const previous = headerLoadTimers.get(regionId);
  if (previous) clearTimeout(previous);
  headerPreviewLoader.invalidate(regionId);
  delete headerErrors[regionId];
  delete startRowValues[regionId];
  delete endRowValues[regionId];
  if (!props.fileId) {
    delete headerLoading[regionId];
    return;
  }
  headerLoading[regionId] = true;
  headerLoadTimers.set(
    regionId,
    setTimeout(() => {
      headerLoadTimers.delete(regionId);
      void loadHeaders(regionId);
    }, 250)
  );
};

watch(
  modelToken,
  token => {
    if (token !== lastEmittedModelToken) resetFromModel();
  },
  { immediate: true }
);
watch(
  () => props.resetVersion,
  () => {
    detailsExpanded.value = false;
    resetFromModel();
  }
);
watch(
  () => props.fileId,
  (fileId, previousFileId) => {
    if (!fileId || fileId === previousFileId) return;
    drafts.value.forEach(draft => scheduleHeaderLoad(draft.regionId));
  }
);

const handleRowChange = (
  index: number,
  field: keyof SmartStructureExcelRowPatch,
  value: number | undefined
) => {
  if (value == null) return;
  const current = drafts.value[index];
  const next = applySmartStructureExcelRowPatch(
    current,
    {
      [field]: value
    },
    bounds.value
  );
  if (field === "dataStartRow" && next.dataStartRow !== value) {
    synchronizationNotices[next.regionId] =
      `数据起始行前必须保留一行表头，已调整为第 ${next.dataStartRow} 行`;
  } else if (
    field === "dataStartRow" &&
    next.dataStartRow !== current.dataStartRow
  ) {
    synchronizationNotices[next.regionId] =
      `表头固定取第 ${next.headerStartRow} 行，列标题已同步刷新`;
  } else if (field === "dataEndRow" && next.dataEndRow !== value) {
    synchronizationNotices[next.regionId] =
      `数据结束行不能早于起始行，已调整为第 ${next.dataEndRow} 行`;
  } else {
    delete synchronizationNotices[next.regionId];
  }
  replaceDraft(index, next);
  if (field === "dataStartRow" || field === "dataEndRow") {
    scheduleHeaderLoad(next.regionId);
  }
};

const handleA1EndpointInput = (
  index: number,
  field: SmartStructureExcelField,
  endpoint: SmartStructureExcelEndpoint,
  value: string
) => {
  if (props.compact) return;
  const draft = drafts.value[index];
  const previousColumn = getColumnValue(draft, field);
  const regionBuffers = (a1EndpointBuffers[draft.regionId] ??= {});
  const fieldBuffer = (regionBuffers[field] ??=
    formatSmartStructureExcelFieldEndpoints(draft, field));
  fieldBuffer[endpoint] = value;

  const regionErrors = (a1Errors[draft.regionId] ??= {});
  const fieldErrors = (regionErrors[field] ??= {});
  if (!value.trim()) {
    const definition = fieldDefinitions.find(item => item.field === field);
    fieldErrors[endpoint] =
      field === "project" && !draft.isSpecificationOnly
        ? "项目单元格不能为空；没有项目列时请明确开启仅规格表"
        : `${definition?.label ?? "字段"}${endpoint === "start" ? "起始" : "结束"}单元格不能为空`;
    return;
  }

  const result = applySmartStructureExcelEndpointPatch(
    draft,
    field,
    endpoint,
    value,
    bounds.value
  );
  if (!result.ok) {
    fieldErrors[endpoint] = result.error;
    return;
  }

  delete fieldErrors[endpoint];
  if (Object.keys(fieldErrors).length === 0) delete regionErrors[field];
  if (result.synchronizedRows) {
    synchronizationNotices[draft.regionId] =
      `已同步本区域全部字段的数据行为第 ${result.draft.dataStartRow}–${result.draft.dataEndRow} 行`;
  } else {
    delete synchronizationNotices[draft.regionId];
  }
  replaceDraft(index, result.draft);
  if (
    result.draft.dataStartRow !== draft.dataStartRow ||
    result.draft.dataEndRow !== draft.dataEndRow ||
    getColumnValue(result.draft, field) !== previousColumn
  ) {
    scheduleHeaderLoad(result.draft.regionId);
  }
};

const handleSpecificationOnlyChange = (index: number, enabled: boolean) => {
  const next = setSmartStructureSpecificationOnly(drafts.value[index], enabled);
  delete a1Errors[next.regionId]?.project;
  delete synchronizationNotices[next.regionId];
  replaceDraft(index, next);
};

const getFieldError = (
  draft: SmartStructureExcelRegionDraft,
  regionIndex: number,
  columnField: SmartStructureExcelRegionValidationField
) =>
  validationIssues.value.find(
    issue => issue.regionIndex === regionIndex && issue.field === columnField
  )?.message;

const getA1FieldError = (
  draft: SmartStructureExcelRegionDraft,
  regionIndex: number,
  field: SmartStructureExcelField,
  columnField: SmartStructureExcelRegionValidationField
) =>
  Object.values(a1Errors[draft.regionId]?.[field] ?? {}).find(Boolean) ||
  getFieldError(draft, regionIndex, columnField);

const getA1EndpointError = (
  draft: SmartStructureExcelRegionDraft,
  regionIndex: number,
  field: SmartStructureExcelField,
  columnField: SmartStructureExcelRegionValidationField,
  endpoint: SmartStructureExcelEndpoint
) =>
  a1Errors[draft.regionId]?.[field]?.[endpoint] ||
  (endpoint === "start"
    ? getFieldError(draft, regionIndex, columnField)
    : undefined);

const getRegionStructureError = (regionIndex: number) =>
  validationIssues.value.find(
    issue =>
      issue.regionIndex === regionIndex &&
      [
        "headerStartRow",
        "headerRowCount",
        "dataStartRow",
        "dataEndRow",
        "region"
      ].includes(issue.field)
  )?.message;

const createRegionId = () => {
  manualRegionSequence += 1;
  return `table-${props.table.tableIndex}-manual-${Date.now()}-${manualRegionSequence}`;
};

const cloneDraft = (
  source: SmartStructureExcelRegionDraft,
  regionId: string
): SmartStructureExcelRegionDraft => ({
  ...source,
  regionId,
  source: {
    ...source.source,
    regionId,
    headers: [...source.source.headers],
    confidence: 1,
    source: "Manual",
    decision: "NeedConfirm",
    fields: source.source.fields.map(field => ({ ...field })),
    issues: [],
    fieldConflicts: []
  }
});

const appendDraft = (next: SmartStructureExcelRegionDraft) => {
  drafts.value = [...drafts.value, next];
  a1EndpointBuffers[next.regionId] = Object.fromEntries(
    fieldDefinitions.map(definition => [
      definition.field,
      formatSmartStructureExcelFieldEndpoints(next, definition.field)
    ])
  );
  a1Errors[next.regionId] = {};
  scheduleHeaderLoad(next.regionId);
  emitRegions();
};

const addRegion = () => {
  const source = drafts.value[drafts.value.length - 1];
  if (!source) return;
  const dataStartRow = source.dataEndRow + 2;
  const rowLength = Math.max(1, source.dataEndRow - source.dataStartRow + 1);
  const fitsBelow = dataStartRow <= maximumRow.value;
  const next = cloneDraft(source, createRegionId());
  appendDraft(
    fitsBelow
      ? applySmartStructureExcelRowPatch(
          next,
          {
            dataStartRow,
            dataEndRow: Math.min(maximumRow.value, dataStartRow + rowLength - 1)
          },
          bounds.value
        )
      : next
  );
};

const copyRegion = (index: number) => {
  appendDraft(cloneDraft(drafts.value[index], createRegionId()));
};

const openCompactRegionDetails = (regionId: string) => {
  if (!props.compact) return;
  selectedCompactRegionId.value =
    selectedCompactRegionId.value === regionId ? null : regionId;
};

const closeCompactRegionDetails = () => {
  selectedCompactRegionId.value = null;
};

const removeRegion = (index: number) => {
  if (drafts.value.length <= 1) return;
  const [removed] = drafts.value.splice(index, 1);
  if (selectedCompactRegionId.value === removed.regionId) {
    closeCompactRegionDetails();
  }
  const timer = headerLoadTimers.get(removed.regionId);
  if (timer) clearTimeout(timer);
  headerLoadTimers.delete(removed.regionId);
  headerPreviewLoader.invalidate(removed.regionId);
  delete a1EndpointBuffers[removed.regionId];
  delete a1Errors[removed.regionId];
  delete headerErrors[removed.regionId];
  delete headerLoading[removed.regionId];
  delete startRowValues[removed.regionId];
  delete endRowValues[removed.regionId];
  delete synchronizationNotices[removed.regionId];
  drafts.value = [...drafts.value];
  emitRegions();
};

onBeforeUnmount(() => {
  headerLoadTimers.forEach(timer => clearTimeout(timer));
  headerLoadTimers.clear();
  drafts.value.forEach(draft => headerPreviewLoader.invalidate(draft.regionId));
});
</script>

<template>
  <section
    class="excel-region-editor"
    :class="{ 'is-compact': compact }"
    aria-label="Excel 结构与列映射"
  >
    <div class="excel-region-editor__summary-heading">
      <div class="excel-region-editor__summary-actions">
        <el-button
          plain
          type="primary"
          size="small"
          :disabled="disabled"
          @click="addRegion"
        >
          ＋ 添加数据区域
        </el-button>
        <el-button
          v-if="!compact"
          plain
          type="primary"
          size="small"
          :aria-expanded="detailsExpanded"
          :aria-controls="detailsControlIds"
          @click="detailsExpanded = !detailsExpanded"
        >
          {{ detailsExpanded ? "收起更多配置" : "展开更多配置" }}
        </el-button>
      </div>
    </div>

    <div
      v-if="detailsExpanded"
      :id="detailsToolbarId"
      class="excel-region-editor__heading"
    >
      <span>表头固定取数据起始行的上一行；数据行、列映射与 A1 会同步。</span>
      <div class="excel-region-editor__tools">
        <el-tag size="small" type="info" effect="plain">
          工作表坐标从 1 开始
        </el-tag>
        <el-button
          link
          type="primary"
          :disabled="disabled"
          @click="emit('reset')"
        >
          重置为识别结果
        </el-button>
      </div>
    </div>

    <article
      v-for="(draft, regionIndex) in drafts"
      :key="draft.regionId"
      class="excel-region-card"
      :class="{
        'is-compact-selected':
          compact && draft.regionId === selectedCompactRegionId
      }"
      :title="compact ? '双击查看并编辑区域配置' : undefined"
      @dblclick="openCompactRegionDetails(draft.regionId)"
    >
      <header v-if="!compact" class="excel-region-card__header">
        <div>
          <span class="excel-region-card__number">{{ regionIndex + 1 }}</span>
          <strong>区域 {{ regionIndex + 1 }}</strong>
          <span v-if="detailsExpanded" class="excel-region-card__rows">
            数据第 {{ draft.dataStartRow }}–{{ draft.dataEndRow }} 行
          </span>
        </div>
      </header>
      <span
        v-else
        class="excel-region-card__number is-gutter"
        :aria-label="`区域 ${regionIndex + 1}`"
      >
        {{ regionIndex + 1 }}
      </span>
      <button
        v-if="compact"
        type="button"
        class="excel-region-card__compact-remove"
        :disabled="disabled || drafts.length <= 1"
        :title="drafts.length <= 1 ? '至少保留一个数据区域' : '删除区域'"
        :aria-label="`删除区域 ${regionIndex + 1}`"
        @click="removeRegion(regionIndex)"
        @dblclick.stop
      >
        <span aria-hidden="true">×</span>
      </button>

      <div class="excel-region-a1-grid">
        <div
          v-for="definition in fieldDefinitions"
          :key="definition.field"
          class="excel-region-a1-field"
        >
          <div class="excel-region-endpoint-row is-start">
            <el-input
              :size="compact ? 'small' : 'default'"
              :readonly="compact"
              :tabindex="compact ? -1 : undefined"
              :model-value="
                a1EndpointBuffers[draft.regionId]?.[definition.field]?.start ??
                ''
              "
              :aria-label="`${definition.label}起始单元格`"
              :aria-describedby="
                getStartCellContentId(regionIndex, definition.field)
              "
              :aria-errormessage="
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  'start'
                )
                  ? getA1EndpointErrorId(regionIndex, definition.field, 'start')
                  : undefined
              "
              :aria-invalid="
                Boolean(
                  getA1EndpointError(
                    draft,
                    regionIndex,
                    definition.field,
                    definition.columnField,
                    'start'
                  )
                )
              "
              :disabled="
                disabled ||
                (definition.field === 'project' && draft.isSpecificationOnly)
              "
              :placeholder="getA1EndpointPlaceholder(definition, 'start')"
              @input="
                value =>
                  handleA1EndpointInput(
                    regionIndex,
                    definition.field,
                    'start',
                    value
                  )
              "
            />
            <span
              :id="getStartCellContentId(regionIndex, definition.field)"
              class="excel-region-start-cell-value"
              :title="getStartCellContent(draft, definition.field)"
            >
              {{ getStartCellContent(draft, definition.field) }}
            </span>
            <span
              v-if="
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  'start'
                )
              "
              :id="getA1EndpointErrorId(regionIndex, definition.field, 'start')"
              class="sr-only"
            >
              {{
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  "start"
                )
              }}
            </span>
          </div>
          <div class="excel-region-endpoint-row is-end">
            <el-input
              :size="compact ? 'small' : 'default'"
              :readonly="compact"
              :tabindex="compact ? -1 : undefined"
              :model-value="
                a1EndpointBuffers[draft.regionId]?.[definition.field]?.end ?? ''
              "
              :aria-label="`${definition.label}结束单元格`"
              :aria-describedby="
                getEndCellContentId(regionIndex, definition.field)
              "
              :aria-errormessage="
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  'end'
                )
                  ? getA1EndpointErrorId(regionIndex, definition.field, 'end')
                  : undefined
              "
              :aria-invalid="
                Boolean(
                  getA1EndpointError(
                    draft,
                    regionIndex,
                    definition.field,
                    definition.columnField,
                    'end'
                  )
                )
              "
              :disabled="
                disabled ||
                (definition.field === 'project' && draft.isSpecificationOnly)
              "
              :placeholder="getA1EndpointPlaceholder(definition, 'end')"
              @input="
                value =>
                  handleA1EndpointInput(
                    regionIndex,
                    definition.field,
                    'end',
                    value
                  )
              "
            />
            <span
              :id="getEndCellContentId(regionIndex, definition.field)"
              class="excel-region-end-cell-value"
              :title="getEndCellContent(draft, definition.field)"
            >
              {{ getEndCellContent(draft, definition.field) }}
            </span>
            <span
              v-if="
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  'end'
                )
              "
              :id="getA1EndpointErrorId(regionIndex, definition.field, 'end')"
              class="sr-only"
            >
              {{
                getA1EndpointError(
                  draft,
                  regionIndex,
                  definition.field,
                  definition.columnField,
                  "end"
                )
              }}
            </span>
          </div>
          <span
            v-if="
              getA1FieldError(
                draft,
                regionIndex,
                definition.field,
                definition.columnField
              )
            "
            class="excel-region-field-error"
            role="alert"
          >
            {{
              getA1FieldError(
                draft,
                regionIndex,
                definition.field,
                definition.columnField
              )
            }}
          </span>
        </div>
      </div>

      <p
        v-if="headerErrors[draft.regionId]"
        class="excel-region-message is-error"
        role="alert"
      >
        {{ headerErrors[draft.regionId] }}
      </p>

      <p
        v-if="synchronizationNotices[draft.regionId]"
        class="excel-region-message is-info"
        aria-live="polite"
      >
        {{ synchronizationNotices[draft.regionId] }}
      </p>

      <p
        v-if="getRegionStructureError(regionIndex)"
        class="excel-region-message is-error"
        role="alert"
      >
        {{ getRegionStructureError(regionIndex) }}
      </p>

      <div
        v-if="detailsExpanded"
        :id="getRegionDetailsId(regionIndex)"
        class="excel-region-details"
      >
        <div class="excel-region-card__actions">
          <el-button
            link
            type="primary"
            :size="compact ? 'small' : 'default'"
            :disabled="disabled"
            @click="copyRegion(regionIndex)"
          >
            复制
          </el-button>
          <el-button
            link
            type="danger"
            :size="compact ? 'small' : 'default'"
            :disabled="disabled || drafts.length <= 1"
            @click="removeRegion(regionIndex)"
          >
            删除
          </el-button>
        </div>

        <div class="excel-region-row-grid">
          <label>
            <span>数据起始行</span>
            <el-input-number
              :size="compact ? 'small' : 'default'"
              :model-value="draft.dataStartRow"
              :min="rowInputLimits(draft).dataStartMinimum"
              :max="rowInputLimits(draft).dataStartMaximum"
              :disabled="disabled"
              controls-position="right"
              @update:model-value="
                value => handleRowChange(regionIndex, 'dataStartRow', value)
              "
            />
          </label>
          <label>
            <span>数据结束行</span>
            <el-input-number
              :size="compact ? 'small' : 'default'"
              :model-value="draft.dataEndRow"
              :min="rowInputLimits(draft).dataEndMinimum"
              :max="rowInputLimits(draft).dataEndMaximum"
              :disabled="disabled"
              controls-position="right"
              @update:model-value="
                value => handleRowChange(regionIndex, 'dataEndRow', value)
              "
            />
          </label>
        </div>

        <div class="excel-region-mapping-grid">
          <template
            v-for="definition in fieldDefinitions"
            :key="definition.field"
          >
            <span class="excel-region-mapping-label">
              {{ definition.label
              }}{{ definition.required ? "列" : "列（可选）" }}
            </span>
            <el-select
              :size="compact ? 'small' : 'default'"
              :model-value="getColumnValue(draft, definition.field)"
              :disabled="
                disabled ||
                headerLoading[draft.regionId] ||
                (definition.field === 'project' && draft.isSpecificationOnly)
              "
              :loading="headerLoading[draft.regionId]"
              clearable
              filterable
              placeholder="请选择列"
              popper-class="smart-structure-column-select-popper"
              @update:model-value="
                value =>
                  handleColumnChange(regionIndex, definition.field, value)
              "
            >
              <el-option
                v-for="option in columnOptions(draft)"
                :key="option.value"
                :value="option.value"
                :label="`[${option.column}] ${option.label}`"
              >
                <span class="column-option-coordinate"
                  >[{{ option.column }}]</span
                >
                <span class="column-option-label">{{ option.label }}</span>
              </el-option>
            </el-select>
          </template>
        </div>

        <div class="excel-region-card__footer">
          <div>
            <span>仅规格表（没有独立项目列）</span>
            <small>必须由用户明确开启，不会因项目列为空自动切换。</small>
          </div>
          <el-switch
            :size="compact ? 'small' : 'default'"
            :model-value="draft.isSpecificationOnly"
            :disabled="disabled"
            :aria-label="`区域 ${regionIndex + 1} 仅规格表`"
            @update:model-value="
              value =>
                handleSpecificationOnlyChange(regionIndex, Boolean(value))
            "
          />
        </div>
      </div>
    </article>

    <section
      v-if="compact && selectedCompactDraft && selectedCompactRegionIndex >= 0"
      class="excel-region-compact-details"
      :aria-label="`区域 ${selectedCompactRegionIndex + 1} 配置`"
    >
      <header class="excel-region-compact-details__header">
        <strong>当前配置：区域 {{ selectedCompactRegionIndex + 1 }}</strong>
        <div class="excel-region-card__actions">
          <el-button
            link
            type="primary"
            size="small"
            :disabled="disabled"
            @click="copyRegion(selectedCompactRegionIndex)"
          >
            复制
          </el-button>
          <el-button
            link
            type="danger"
            size="small"
            :disabled="disabled || drafts.length <= 1"
            @click="removeRegion(selectedCompactRegionIndex)"
          >
            删除
          </el-button>
          <el-button
            link
            size="small"
            class="excel-region-compact-details__close"
            @click="closeCompactRegionDetails"
          >
            收起
          </el-button>
        </div>
      </header>

      <div class="excel-region-compact-details__fields">
        <label class="excel-region-compact-details__field">
          <span>数据起始行</span>
          <el-input-number
            size="small"
            :model-value="selectedCompactDraft.dataStartRow"
            :min="rowInputLimits(selectedCompactDraft).dataStartMinimum"
            :max="rowInputLimits(selectedCompactDraft).dataStartMaximum"
            :disabled="disabled"
            controls-position="right"
            @update:model-value="
              value =>
                handleRowChange(
                  selectedCompactRegionIndex,
                  'dataStartRow',
                  value
                )
            "
          />
        </label>
        <label class="excel-region-compact-details__field">
          <span>数据结束行</span>
          <el-input-number
            size="small"
            :model-value="selectedCompactDraft.dataEndRow"
            :min="rowInputLimits(selectedCompactDraft).dataEndMinimum"
            :max="rowInputLimits(selectedCompactDraft).dataEndMaximum"
            :disabled="disabled"
            controls-position="right"
            @update:model-value="
              value =>
                handleRowChange(selectedCompactRegionIndex, 'dataEndRow', value)
            "
          />
        </label>

        <label
          v-for="definition in fieldDefinitions"
          :key="definition.field"
          class="excel-region-compact-details__field"
        >
          <span>
            {{ definition.label
            }}{{ definition.required ? "列" : "列（可选）" }}
          </span>
          <el-select
            size="small"
            :model-value="
              getColumnValue(selectedCompactDraft, definition.field)
            "
            :disabled="
              disabled ||
              headerLoading[selectedCompactDraft.regionId] ||
              (definition.field === 'project' &&
                selectedCompactDraft.isSpecificationOnly)
            "
            :loading="headerLoading[selectedCompactDraft.regionId]"
            clearable
            filterable
            placeholder="请选择列"
            popper-class="smart-structure-column-select-popper"
            @update:model-value="
              value =>
                handleColumnChange(
                  selectedCompactRegionIndex,
                  definition.field,
                  value
                )
            "
          >
            <el-option
              v-for="option in columnOptions(selectedCompactDraft)"
              :key="option.value"
              :value="option.value"
              :label="`[${option.column}] ${option.label}`"
            >
              <span class="column-option-coordinate"
                >[{{ option.column }}]</span
              >
              <span class="column-option-label">{{ option.label }}</span>
            </el-option>
          </el-select>
        </label>

        <label class="excel-region-compact-details__specification-only">
          <span>仅规格表（没有独立项目列）</span>
          <el-switch
            size="small"
            :model-value="selectedCompactDraft.isSpecificationOnly"
            :disabled="disabled"
            :aria-label="`区域 ${selectedCompactRegionIndex + 1} 仅规格表`"
            @update:model-value="
              value =>
                handleSpecificationOnlyChange(
                  selectedCompactRegionIndex,
                  Boolean(value)
                )
            "
          />
        </label>
      </div>
    </section>
  </section>
</template>

<style scoped>
.excel-region-editor {
  display: grid;
  gap: 12px;
  padding: 12px;
  margin: 10px 0;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.excel-region-editor.is-compact {
  box-sizing: border-box;
  gap: 8px;
  width: min(100%, 980px);
  padding: 8px;
  margin: 8px 0;
  font-size: 12px;
}

.excel-region-editor.is-compact .excel-region-card {
  position: relative;
  width: calc(100% - 28px);
  padding: 8px 32px 8px 8px;
  margin-left: 28px;
  overflow: visible;
}

.excel-region-editor.is-compact .excel-region-card.is-compact-selected {
  border-color: color-mix(in srgb, var(--app-primary) 55%, var(--app-border));
  box-shadow: 0 0 0 1px color-mix(in srgb, var(--app-primary) 12%, transparent);
}

.excel-region-editor.is-compact .excel-region-card__header {
  padding-bottom: 6px;
  margin-bottom: 8px;
}

.excel-region-editor.is-compact .excel-region-card__header strong {
  font-size: 13px;
  line-height: 20px;
}

.excel-region-editor.is-compact .excel-region-card__number {
  width: 20px;
  height: 20px;
  font-size: 12px;
}

.excel-region-editor.is-compact .excel-region-card__number.is-gutter {
  position: absolute;
  top: 50%;
  left: -28px;
  z-index: 1;
  border: 1px solid color-mix(in srgb, var(--app-primary) 45%, transparent);
  transform: translateY(-50%);
}

.excel-region-card__compact-remove {
  position: absolute;
  top: 4px;
  right: 4px;
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  padding: 0;
  font-size: 17px;
  line-height: 1;
  color: var(--app-danger);
  cursor: pointer;
  background: transparent;
  border: 0;
  border-radius: 5px;
}

.excel-region-card__compact-remove:hover:not(:disabled),
.excel-region-card__compact-remove:focus-visible {
  outline: none;
  background: var(--app-danger-bg);
}

.excel-region-card__compact-remove:disabled {
  color: var(--app-text-placeholder);
  cursor: not-allowed;
}

.excel-region-compact-details {
  display: grid;
  gap: 8px;
  padding: 8px 10px;
  margin-top: 2px;
  background: color-mix(in srgb, var(--app-info-bg) 55%, transparent);
  border: 1px solid
    color-mix(in srgb, var(--app-primary) 28%, var(--app-border));
  border-radius: 8px;
}

.excel-region-compact-details__header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding-bottom: 6px;
  border-bottom: 1px dashed var(--app-border);
}

.excel-region-compact-details__header strong {
  font-size: 13px;
  color: var(--app-text-primary);
}

.excel-region-compact-details__fields {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 7px 10px;
  align-items: center;
}

.excel-region-compact-details__field {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 6px;
  align-items: center;
  min-width: 0;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.excel-region-compact-details__field > span {
  white-space: nowrap;
}

.excel-region-compact-details__field :deep(.el-input-number),
.excel-region-compact-details__field :deep(.el-select) {
  width: 100%;
  min-width: 0;
}

.excel-region-compact-details__specification-only {
  display: flex;
  grid-row: 1;
  grid-column: 3 / span 2;
  gap: 8px;
  align-items: center;
  justify-content: flex-start;
  min-width: 0;
  font-size: 12px;
  color: var(--app-text-primary);
}

.excel-region-editor.is-compact .excel-region-a1-grid {
  gap: 6px 10px;
}

.excel-region-editor.is-compact .excel-region-endpoint-row {
  gap: 6px;
}

.excel-region-editor.is-compact .excel-region-endpoint-row.is-start,
.excel-region-editor.is-compact .excel-region-endpoint-row.is-end {
  grid-template-columns: 56px minmax(0, 1fr);
}

.excel-region-editor.is-compact :deep(.el-input__inner),
.excel-region-editor.is-compact :deep(.el-button),
.excel-region-editor.is-compact :deep(.el-select__placeholder) {
  font-size: 12px;
}

.excel-region-editor.is-compact
  .excel-region-endpoint-row
  :deep(.el-input__wrapper) {
  cursor: default;
  background: var(--app-bg-page);
}

.excel-region-editor.is-compact
  .excel-region-endpoint-row
  :deep(.el-input__inner) {
  cursor: default;
}

.excel-region-editor__summary-heading,
.excel-region-editor__heading,
.excel-region-card__header,
.excel-region-card__footer {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.excel-region-editor__heading > div {
  display: grid;
  gap: 3px;
}

.excel-region-editor__summary-heading {
  justify-content: flex-end;
}

.excel-region-editor__summary-actions,
.excel-region-editor__tools {
  display: flex !important;
  gap: 8px !important;
  align-items: center;
}

.excel-region-editor__summary-actions {
  flex-wrap: wrap;
  justify-content: flex-end;
}

.excel-region-editor__heading span,
.excel-region-card__rows,
.excel-region-card__footer small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.excel-region-card {
  min-width: 0;
  padding: 12px;
  background: color-mix(in srgb, var(--app-info-bg) 45%, transparent);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.excel-region-card__header {
  padding-bottom: 10px;
  margin-bottom: 12px;
  border-bottom: 1px solid var(--app-border);
}

.excel-region-card__header > div:first-child,
.excel-region-card__actions {
  display: flex;
  gap: 8px;
  align-items: center;
}

.excel-region-card__actions {
  justify-content: flex-end;
}

.excel-region-card__number {
  display: inline-grid;
  place-items: center;
  width: 22px;
  height: 22px;
  color: var(--app-primary);
  background: var(--app-primary-light);
  border-radius: 50%;
}

.excel-region-a1-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
}

.excel-region-a1-field {
  display: grid;
  gap: 4px;
  align-content: start;
  min-width: 0;
}

.excel-region-a1-field :deep(.el-input) {
  width: 100%;
  min-width: 0;
}

.excel-region-endpoint-row {
  display: grid;
  gap: 8px;
  align-items: center;
  min-width: 0;
}

.excel-region-endpoint-row.is-start {
  grid-template-columns: minmax(84px, 112px) minmax(0, 1fr);
}

.excel-region-endpoint-row.is-end {
  grid-template-columns: minmax(84px, 112px) minmax(0, 1fr);
}

.excel-region-start-cell-value,
.excel-region-end-cell-value {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  line-height: 20px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  overflow: hidden;
  white-space: nowrap;
  border: 0;
  clip: rect(0, 0, 0, 0);
}

.excel-region-details {
  display: grid;
  gap: 12px;
  padding-top: 12px;
  margin-top: 12px;
  border-top: 1px dashed var(--app-border);
}

.excel-region-row-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(180px, 1fr));
  gap: 10px;
}

.excel-region-row-grid label {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr);
  gap: 8px;
  align-items: center;
  min-width: 0;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.excel-region-row-grid :deep(.el-input-number) {
  width: 100%;
}

.excel-region-mapping-grid {
  display: grid;
  grid-template-columns: minmax(84px, auto) minmax(180px, 1fr);
  gap: 8px 10px;
  align-items: center;
}

.excel-region-mapping-label {
  align-self: center;
  font-size: 12px;
  font-weight: 600;
  color: var(--app-text-secondary);
}

.excel-region-mapping-grid :deep(.el-select) {
  width: 100%;
  min-width: 0;
}

.excel-region-field-error {
  font-size: 12px;
  line-height: 16px;
  color: var(--app-danger);
}

.excel-region-card__footer {
  padding-top: 10px;
  margin-top: 6px;
  border-top: 1px dashed var(--app-border);
}

.excel-region-card__footer > div {
  display: grid;
  gap: 2px;
  font-size: 12px;
  color: var(--app-text-primary);
}

.excel-region-message {
  margin: 8px 0 0;
  font-size: 12px;
  line-height: 1.5;
}

.excel-region-message.is-error {
  color: var(--app-danger);
}

.excel-region-message.is-info {
  color: var(--app-primary);
}

@media (width <= 1100px) {
  .excel-region-row-grid {
    grid-template-columns: repeat(2, minmax(150px, 1fr));
  }
}

@media (width <= 760px) {
  .excel-region-editor__heading,
  .excel-region-editor__summary-heading,
  .excel-region-card__header,
  .excel-region-card__footer {
    align-items: flex-start;
  }

  .excel-region-row-grid {
    grid-template-columns: 1fr;
  }

  .excel-region-mapping-grid {
    grid-template-columns: 84px minmax(0, 1fr);
  }

  .excel-region-a1-grid {
    grid-template-columns: 1fr;
  }

  .excel-region-editor__summary-actions {
    width: 100%;
  }
}
</style>

<style>
.smart-structure-column-select-popper {
  min-width: 280px !important;
}

.smart-structure-column-select-popper .el-select-dropdown__item {
  display: flex;
  gap: 8px;
  align-items: flex-start;
  height: auto;
  min-height: 34px;
  padding-top: 7px;
  padding-bottom: 7px;
  line-height: 20px;
  white-space: normal;
}

.smart-structure-column-select-popper .column-option-coordinate {
  flex: none;
  font-weight: 700;
  color: var(--app-primary);
}

.smart-structure-column-select-popper .column-option-label {
  min-width: 0;
  overflow-wrap: anywhere;
}
</style>
