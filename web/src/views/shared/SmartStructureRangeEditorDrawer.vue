<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { getTablePreview, type TableInfo } from "@/api/document";
import type {
  SmartConfigRecognizedField,
  SmartConfigRecognizedRegion,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  findNearestSmartStructureHeaderRowIndex,
  parseExcelA1ColumnRange,
  resolveSmartStructureExcelRangeMapping,
  toExcelColumnLabel,
  validateSmartStructureExcelRanges,
  type SmartStructureExcelRangeField
} from "./smart-structure-recognition";

const props = withDefaults(
  defineProps<{
    modelValue: boolean;
    table: SmartConfigRecognizedTable;
    tableInfo?: TableInfo;
    fileId?: number;
    isExcelFile?: boolean;
    regions: SmartConfigRecognizedRegion[];
  }>(),
  { isExcelFile: true }
);

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
  save: [regions: SmartConfigRecognizedRegion[]];
}>();

type RegionDraft = {
  source: SmartConfigRecognizedRegion;
  headerStartRow: number;
  headerEndRow: number;
  dataStartRow: number;
  dataEndRow: number;
  projectColumnIndex?: number;
  specificationColumnIndex?: number;
  acceptanceColumnIndex?: number;
  remarkColumnIndex?: number;
  projectRange: string;
  specificationRange: string;
  acceptanceRange: string;
  remarkRange: string;
  rangeError: string;
  isSpecificationOnly: boolean;
  headersLoading: boolean;
  headerRequestVersion: number;
};

const drafts = ref<RegionDraft[]>([]);
let draftSequence = 0;
const headerLoadTimers = new Map<string, ReturnType<typeof setTimeout>>();
const saving = ref(false);

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});

const baseRow = computed(() => props.tableInfo?.usedRangeStartRow ?? 1);
const baseColumn = computed(() => props.tableInfo?.usedRangeStartColumn ?? 1);
const maximumRow = computed(
  () => baseRow.value + Math.max(1, props.tableInfo?.rowCount ?? 1) - 1
);
const columnCount = computed(() =>
  Math.max(
    1,
    props.tableInfo?.columnCount ?? 0,
    props.table.headers.length,
    ...props.regions.map(region => region.headers.length)
  )
);

const baseColumnOptions = computed(() =>
  Array.from({ length: columnCount.value }, (_, index) => ({
    value: index,
    column: props.isExcelFile
      ? toExcelColumnLabel(baseColumn.value + index)
      : `第 ${index + 1} 列`,
    label:
      props.table.headers[index] ||
      props.tableInfo?.headers[index] ||
      `列 ${index + 1}`
  }))
);

const getColumnOptions = (draft: RegionDraft) =>
  baseColumnOptions.value.map(option => ({
    ...option,
    label:
      draft.source.headers[option.value] ||
      props.tableInfo?.headers[option.value] ||
      option.label
  }));

const formatExcelDataRange = (
  dataStartRow: number,
  dataEndRow: number,
  columnIndex?: number | null
) => {
  if (columnIndex == null) return "";
  const column = toExcelColumnLabel(baseColumn.value + columnIndex);
  return `${column}${dataStartRow}:${column}${dataEndRow}`;
};

const toDraft = (region: SmartConfigRecognizedRegion): RegionDraft => {
  const headerStartRow = baseRow.value + region.headerRowIndex;
  const dataStartRow = baseRow.value + region.dataStartRowIndex;
  return {
    source: { ...region, headers: [...region.headers] },
    headerStartRow,
    headerEndRow: headerStartRow + Math.max(1, region.headerRowCount) - 1,
    dataStartRow,
    dataEndRow:
      baseRow.value +
      (region.dataEndRowIndex ?? Math.max(0, maximumRow.value - baseRow.value)),
    projectColumnIndex: region.projectColumnIndex ?? undefined,
    specificationColumnIndex: region.specificationColumnIndex ?? undefined,
    acceptanceColumnIndex: region.acceptanceColumnIndex ?? undefined,
    remarkColumnIndex: region.remarkColumnIndex ?? undefined,
    projectRange: formatExcelDataRange(
      dataStartRow,
      baseRow.value +
        (region.dataEndRowIndex ??
          Math.max(0, maximumRow.value - baseRow.value)),
      region.projectColumnIndex
    ),
    specificationRange: formatExcelDataRange(
      dataStartRow,
      baseRow.value +
        (region.dataEndRowIndex ??
          Math.max(0, maximumRow.value - baseRow.value)),
      region.specificationColumnIndex
    ),
    acceptanceRange: formatExcelDataRange(
      dataStartRow,
      baseRow.value +
        (region.dataEndRowIndex ??
          Math.max(0, maximumRow.value - baseRow.value)),
      region.acceptanceColumnIndex
    ),
    remarkRange: formatExcelDataRange(
      dataStartRow,
      baseRow.value +
        (region.dataEndRowIndex ??
          Math.max(0, maximumRow.value - baseRow.value)),
      region.remarkColumnIndex
    ),
    rangeError: "",
    isSpecificationOnly: region.isSpecificationOnly,
    headersLoading: false,
    headerRequestVersion: 0
  };
};

const clearScheduledHeaderLoads = () => {
  headerLoadTimers.forEach(timer => clearTimeout(timer));
  headerLoadTimers.clear();
};

let saveRequestVersion = 0;

const resetDrafts = () => {
  clearScheduledHeaderLoads();
  drafts.value = props.regions.map(toDraft);
};

watch(
  () => props.modelValue,
  value => {
    if (value) resetDrafts();
    else {
      saveRequestVersion += 1;
      saving.value = false;
      drafts.value.forEach(draft => {
        draft.headerRequestVersion += 1;
      });
      clearScheduledHeaderLoads();
    }
  }
);

const normalizeHeaders = (headers: string[], previewColumnCount: number) =>
  Array.from(
    {
      length: Math.max(headers.length, previewColumnCount, columnCount.value)
    },
    (_, index) => headers[index] ?? ""
  );

const getHeaderProbes = (draft: RegionDraft) =>
  [
    draft.isSpecificationOnly
      ? undefined
      : {
          columnIndex: draft.projectColumnIndex,
          expectedHeader:
            draft.source.headers[draft.projectColumnIndex ?? -1] ?? ""
        },
    {
      columnIndex: draft.specificationColumnIndex,
      expectedHeader:
        draft.source.headers[draft.specificationColumnIndex ?? -1] ?? ""
    },
    {
      columnIndex: draft.acceptanceColumnIndex,
      expectedHeader:
        draft.source.headers[draft.acceptanceColumnIndex ?? -1] ?? ""
    },
    draft.remarkColumnIndex == null
      ? undefined
      : {
          columnIndex: draft.remarkColumnIndex,
          expectedHeader: draft.source.headers[draft.remarkColumnIndex] ?? ""
        }
  ].filter((probe): probe is NonNullable<typeof probe> => Boolean(probe));

const resolveExcelHeaderForDraft = async (
  draft: RegionDraft,
  requestVersion: number
) => {
  if (!props.fileId) return true;

  let searchEndRow = draft.dataStartRow - 1;
  const probes = getHeaderProbes(draft);
  const searchWindowSize = 50;

  while (searchEndRow >= baseRow.value) {
    const searchStartRow = Math.max(
      baseRow.value,
      searchEndRow - searchWindowSize + 1
    );
    const previewRowCount = searchEndRow - searchStartRow + 1;
    const res = await getTablePreview(props.fileId, props.table.tableIndex, {
      previewRows: previewRowCount,
      headerRowIndex: Math.max(0, draft.source.headerRowIndex),
      headerRowCount: 1,
      dataStartRowIndex: searchStartRow - baseRow.value,
      dataEndRowIndex: searchEndRow - baseRow.value
    });
    if (
      requestVersion !== draft.headerRequestVersion ||
      !drafts.value.includes(draft)
    ) {
      return false;
    }
    if (res.code !== 0) {
      throw new Error(res.message || "加载表头候选失败");
    }

    const matchedRowIndex = findNearestSmartStructureHeaderRowIndex(
      res.data.rows,
      probes
    );
    if (matchedRowIndex != null) {
      const headerRow = searchStartRow + matchedRowIndex;
      draft.headerStartRow = headerRow;
      draft.headerEndRow = headerRow;
      draft.source = {
        ...draft.source,
        headers: normalizeHeaders(
          res.data.rows[matchedRowIndex] ?? [],
          res.data.columnCount
        )
      };
      return true;
    }

    searchEndRow = searchStartRow - 1;
  }

  draft.rangeError = "未在数据范围上方找到与当前列映射匹配的有效表头";
  throw new Error(draft.rangeError);
};

const rebuildRecognizedFields = (
  draft: RegionDraft
): SmartConfigRecognizedField[] => {
  const definitions: Array<{
    field: SmartConfigRecognizedField["field"];
    columnIndex?: number;
  }> = [
    {
      field: "Project",
      columnIndex: draft.isSpecificationOnly
        ? undefined
        : draft.projectColumnIndex
    },
    { field: "Specification", columnIndex: draft.specificationColumnIndex },
    { field: "Acceptance", columnIndex: draft.acceptanceColumnIndex },
    { field: "Remark", columnIndex: draft.remarkColumnIndex }
  ];

  return definitions.map(definition => {
    const previous = draft.source.fields.find(
      field => field.field === definition.field
    );
    return {
      field: definition.field,
      columnIndex: definition.columnIndex,
      header:
        definition.columnIndex == null
          ? undefined
          : draft.source.headers[definition.columnIndex] || undefined,
      confidence: previous?.confidence ?? 1,
      source: previous?.source ?? "Manual"
    };
  });
};

const loadHeadersForDraft = async (draft: RegionDraft) => {
  if (!props.fileId) return true;
  const requestVersion = ++draft.headerRequestVersion;
  draft.headersLoading = true;
  try {
    if (props.isExcelFile) {
      return await resolveExcelHeaderForDraft(draft, requestVersion);
    }

    const headerRowIndex = draft.headerStartRow - baseRow.value;
    const headerRowCount = draft.headerEndRow - draft.headerStartRow + 1;
    const res = await getTablePreview(props.fileId, props.table.tableIndex, {
      previewRows: 1,
      headerRowIndex,
      headerRowCount,
      dataStartRowIndex: draft.headerEndRow - baseRow.value + 1
    });
    if (
      requestVersion !== draft.headerRequestVersion ||
      !drafts.value.includes(draft)
    ) {
      return false;
    }
    if (res.code !== 0) {
      throw new Error(res.message || "加载区域表头失败");
    }
    draft.source = {
      ...draft.source,
      headers: normalizeHeaders(res.data.headers, res.data.columnCount)
    };
    return true;
  } catch (error) {
    if (requestVersion === draft.headerRequestVersion) {
      ElMessage.error(
        error instanceof Error ? error.message : "加载区域表头失败"
      );
    }
    return false;
  } finally {
    if (requestVersion === draft.headerRequestVersion) {
      draft.headersLoading = false;
    }
  }
};

const scheduleHeaderLoad = (draft: RegionDraft) => {
  const key = draft.source.regionId;
  const previous = headerLoadTimers.get(key);
  if (previous) clearTimeout(previous);
  headerLoadTimers.set(
    key,
    setTimeout(() => {
      headerLoadTimers.delete(key);
      void loadHeadersForDraft(draft);
    }, 250)
  );
};

watch(
  () =>
    drafts.value.map(draft => ({
      id: draft.source.regionId,
      start: draft.headerStartRow,
      end: draft.headerEndRow
    })),
  (current, previous = []) => {
    const previousById = new Map(previous.map(item => [item.id, item]));
    current.forEach((item, index) => {
      const old = previousById.get(item.id);
      if (
        !props.isExcelFile &&
        old &&
        (old.start !== item.start || old.end !== item.end)
      ) {
        const draft = drafts.value[index];
        if (draft.headerEndRow >= draft.headerStartRow) {
          scheduleHeaderLoad(draft);
        }
      }
    });
  }
);

const formatColumnRange = (draft: RegionDraft, columnIndex?: number | null) => {
  if (columnIndex == null) return "-";
  if (!props.isExcelFile) {
    return `第 ${columnIndex + 1} 列（第 ${draft.dataStartRow}–${draft.dataEndRow} 行）`;
  }
  const column = toExcelColumnLabel(baseColumn.value + columnIndex);
  return `${column}${draft.dataStartRow}:${column}${draft.dataEndRow}`;
};

const regionPreview = (draft: RegionDraft) =>
  (props.isExcelFile
    ? [
        draft.projectRange,
        draft.specificationRange,
        draft.acceptanceRange,
        draft.remarkRange
      ]
    : [
        formatColumnRange(draft, draft.projectColumnIndex),
        formatColumnRange(draft, draft.specificationColumnIndex),
        formatColumnRange(draft, draft.acceptanceColumnIndex),
        formatColumnRange(draft, draft.remarkColumnIndex)
      ]
  )
    .filter(value => value !== "-")
    .filter(Boolean)
    .join(" · ");

type ExcelRangeField =
  | "projectRange"
  | "specificationRange"
  | "acceptanceRange"
  | "remarkRange";

const normalizeRangeInput = (draft: RegionDraft, field: ExcelRangeField) => {
  draft.rangeError = "";
  const parsed = parseExcelA1ColumnRange(draft[field]);
  if (parsed) draft[field] = parsed.normalized;
};

const excelRangeValidations = computed(() =>
  drafts.value.map(draft =>
    validateSmartStructureExcelRanges(
      {
        projectRange: draft.projectRange,
        specificationRange: draft.specificationRange,
        acceptanceRange: draft.acceptanceRange,
        remarkRange: draft.remarkRange
      },
      {
        baseColumn: baseColumn.value,
        columnCount: columnCount.value,
        baseRow: baseRow.value,
        maximumRow: maximumRow.value
      }
    )
  )
);

const getRangeFieldError = (
  regionIndex: number,
  field: SmartStructureExcelRangeField
) => excelRangeValidations.value[regionIndex]?.fieldErrors[field] ?? "";

const getRangeFieldErrorId = (
  regionIndex: number,
  field: SmartStructureExcelRangeField
) => `region-${regionIndex}-${field}-error`;

const liveExcelValidationError = computed(() => {
  for (const [index, validation] of excelRangeValidations.value.entries()) {
    const error = Object.values(validation.fieldErrors)[0];
    if (error) return `区域 ${index + 1}：${error}`;
  }

  const intervals = excelRangeValidations.value
    .map((validation, index) => {
      const parsed =
        validation.parsedRanges.projectRange ??
        validation.parsedRanges.specificationRange ??
        validation.parsedRanges.acceptanceRange ??
        validation.parsedRanges.remarkRange;
      return parsed
        ? {
            regionIndex: index,
            startRow: parsed.startRow,
            endRow: parsed.endRow
          }
        : undefined;
    })
    .filter((value): value is NonNullable<typeof value> => value != null)
    .sort((left, right) => left.startRow - right.startRow);

  for (let index = 1; index < intervals.length; index += 1) {
    if (intervals[index].startRow <= intervals[index - 1].endRow) {
      return `区域 ${intervals[index - 1].regionIndex + 1}与区域 ${intervals[index].regionIndex + 1}的数据范围不能重叠`;
    }
  }
  return "";
});

const saveDisabled = computed(
  () =>
    saving.value ||
    (props.isExcelFile && liveExcelValidationError.value.length > 0)
);

const applyExcelRanges = (draft: RegionDraft, regionIndex: number) => {
  draft.rangeError = "";
  const rangeFields: ExcelRangeField[] = [
    "projectRange",
    "specificationRange",
    "acceptanceRange",
    "remarkRange"
  ];
  const validation = resolveSmartStructureExcelRangeMapping(
    {
      projectRange: draft.projectRange,
      specificationRange: draft.specificationRange,
      acceptanceRange: draft.acceptanceRange,
      remarkRange: draft.remarkRange
    },
    {
      baseColumn: baseColumn.value,
      columnCount: columnCount.value,
      baseRow: baseRow.value,
      maximumRow: maximumRow.value
    }
  );
  const invalidField = rangeFields.find(field => validation.fieldErrors[field]);
  if (invalidField) {
    draft.rangeError = validation.fieldErrors[invalidField] ?? "";
    return `区域 ${regionIndex + 1}的${draft.rangeError}`;
  }

  for (const field of rangeFields) {
    draft[field] = validation.normalizedRanges?.[field] ?? "";
  }
  draft.projectColumnIndex = validation.projectColumnIndex;
  draft.specificationColumnIndex = validation.specificationColumnIndex;
  draft.acceptanceColumnIndex = validation.acceptanceColumnIndex;
  draft.remarkColumnIndex = validation.remarkColumnIndex;
  draft.dataStartRow = validation.dataStartRow!;
  draft.dataEndRow = validation.dataEndRow!;
  draft.isSpecificationOnly = validation.isSpecificationOnly!;
  return "";
};

const addRegion = () => {
  const last = drafts.value.at(-1);
  const headerStartRow = last ? last.dataEndRow + 1 : baseRow.value;
  const dataStartRow = headerStartRow + 1;
  if (dataStartRow > maximumRow.value) {
    ElMessage.warning("已到工作表末行，请先缩短现有区域再添加");
    return;
  }

  const source = last?.source ?? props.regions[0];
  if (!source) return;
  const draft: RegionDraft = {
    source: {
      ...source,
      regionId: `table-${props.table.tableIndex}-region-new-${++draftSequence}`,
      regionIndex: drafts.value.length,
      headers: [...source.headers]
    },
    headerStartRow,
    headerEndRow: headerStartRow,
    dataStartRow,
    dataEndRow: maximumRow.value,
    projectColumnIndex: last?.projectColumnIndex,
    specificationColumnIndex: last?.specificationColumnIndex,
    acceptanceColumnIndex: last?.acceptanceColumnIndex,
    remarkColumnIndex: last?.remarkColumnIndex,
    projectRange: formatExcelDataRange(
      dataStartRow,
      maximumRow.value,
      last?.projectColumnIndex
    ),
    specificationRange: formatExcelDataRange(
      dataStartRow,
      maximumRow.value,
      last?.specificationColumnIndex
    ),
    acceptanceRange: formatExcelDataRange(
      dataStartRow,
      maximumRow.value,
      last?.acceptanceColumnIndex
    ),
    remarkRange: formatExcelDataRange(
      dataStartRow,
      maximumRow.value,
      last?.remarkColumnIndex
    ),
    rangeError: "",
    isSpecificationOnly: last?.isSpecificationOnly ?? false,
    headersLoading: false,
    headerRequestVersion: 0
  };
  drafts.value.push(draft);
  scheduleHeaderLoad(draft);
};

const removeRegion = (index: number) => {
  if (drafts.value.length <= 1) {
    ElMessage.warning("至少保留一个数据区域");
    return;
  }
  const [removed] = drafts.value.splice(index, 1);
  const timer = removed && headerLoadTimers.get(removed.source.regionId);
  if (timer) clearTimeout(timer);
  if (removed) headerLoadTimers.delete(removed.source.regionId);
};

const validateDrafts = () => {
  for (const [index, draft] of drafts.value.entries()) {
    const label = `区域 ${index + 1}`;
    if (props.isExcelFile) {
      const rangeError = applyExcelRanges(draft, index);
      if (rangeError) return rangeError;
    }
    if (props.isExcelFile) {
      if (
        draft.dataStartRow <= baseRow.value ||
        draft.dataEndRow < draft.dataStartRow ||
        draft.dataEndRow > maximumRow.value
      ) {
        return `${label}的数据行范围无效`;
      }
    } else if (
      draft.headerStartRow < baseRow.value ||
      draft.headerEndRow < draft.headerStartRow ||
      draft.dataStartRow <= draft.headerEndRow ||
      draft.dataEndRow < draft.dataStartRow ||
      draft.dataEndRow > maximumRow.value
    ) {
      return `${label}的表头或数据行范围无效`;
    }
    if (draft.specificationColumnIndex == null) {
      return `${label}请选择规格列`;
    }
    if (draft.acceptanceColumnIndex == null) {
      return `${label}请选择验收列`;
    }
    if (!draft.isSpecificationOnly && draft.projectColumnIndex == null) {
      return `${label}请选择项目列`;
    }
    const columns = [
      draft.isSpecificationOnly ? undefined : draft.projectColumnIndex,
      draft.specificationColumnIndex,
      draft.acceptanceColumnIndex,
      draft.remarkColumnIndex
    ].filter((value): value is number => value != null);
    if (new Set(columns).size !== columns.length) {
      return `${label}的字段列不能重复`;
    }
  }

  const ordered = [...drafts.value].sort((left, right) =>
    props.isExcelFile
      ? left.dataStartRow - right.dataStartRow
      : left.headerStartRow - right.headerStartRow
  );
  for (let index = 1; index < ordered.length; index += 1) {
    const nextStart = props.isExcelFile
      ? ordered[index].dataStartRow
      : ordered[index].headerStartRow;
    if (nextStart <= ordered[index - 1].dataEndRow) {
      return "数据区域之间不能重叠";
    }
  }
  return "";
};

const saveRanges = async () => {
  const error = validateDrafts();
  if (error) {
    ElMessage.warning(error);
    return;
  }

  clearScheduledHeaderLoads();
  const requestVersion = ++saveRequestVersion;
  saving.value = true;
  const headerResults = await Promise.all(
    drafts.value.map(loadHeadersForDraft)
  );
  if (requestVersion !== saveRequestVersion || !visible.value) {
    return;
  }
  saving.value = false;
  if (headerResults.some(result => !result)) {
    ElMessage.warning("区域表头尚未加载完成，请稍后重试");
    return;
  }

  const resolvedHeaderError = [...drafts.value]
    .sort((left, right) => left.headerStartRow - right.headerStartRow)
    .find(
      (draft, index, ordered) =>
        draft.headerStartRow < baseRow.value ||
        draft.headerEndRow >= draft.dataStartRow ||
        (index > 0 && draft.headerStartRow <= ordered[index - 1].dataEndRow)
    );
  if (resolvedHeaderError) {
    ElMessage.warning("反推的表头与数据区域重叠，请检查范围");
    return;
  }

  const regions = [...drafts.value]
    .sort((left, right) => left.headerStartRow - right.headerStartRow)
    .map(
      (draft, index): SmartConfigRecognizedRegion => ({
        ...draft.source,
        regionIndex: index,
        headerRowIndex: draft.headerStartRow - baseRow.value,
        headerRowCount: draft.headerEndRow - draft.headerStartRow + 1,
        dataStartRowIndex: draft.dataStartRow - baseRow.value,
        dataEndRowIndex: draft.dataEndRow - baseRow.value,
        projectColumnIndex: draft.isSpecificationOnly
          ? undefined
          : draft.projectColumnIndex,
        specificationColumnIndex: draft.specificationColumnIndex,
        acceptanceColumnIndex: draft.acceptanceColumnIndex,
        remarkColumnIndex: draft.remarkColumnIndex,
        isSpecificationOnly: draft.isSpecificationOnly,
        fields: rebuildRecognizedFields(draft)
      })
    );

  emit("save", regions);
  visible.value = false;
  ElMessage.success("范围已更新，请确认后保存并学习");
};
</script>

<template>
  <el-drawer
    v-model="visible"
    class="smart-structure-range-drawer"
    title="调整识别范围"
    size="min(760px, 94vw)"
    append-to-body
    destroy-on-close
  >
    <div v-if="!isExcelFile" class="range-editor-intro">
      <strong>按表格中的真实行号调整</strong>
      <span> 修改后下方行列范围会同步变化；区域之间不能重叠。 </span>
    </div>

    <div class="region-editor-list">
      <section
        v-for="(draft, index) in drafts"
        :key="draft.source.regionId"
        class="region-editor-card"
        :class="{ 'region-editor-card--excel': isExcelFile }"
      >
        <header v-if="!isExcelFile" class="region-editor-header">
          <div>
            <span class="region-number">{{ index + 1 }}</span>
            <strong>区域 {{ index + 1 }}</strong>
            <code>{{ regionPreview(draft) }}</code>
          </div>
          <el-button
            type="danger"
            link
            :disabled="drafts.length <= 1"
            @click="removeRegion(index)"
          >
            删除区域
          </el-button>
        </header>

        <template v-if="isExcelFile">
          <div class="excel-region-row">
            <span class="excel-region-index">{{ index + 1 }}</span>
            <div class="excel-region-fields">
              <div class="excel-region-field">
                <el-input
                  v-model="draft.projectRange"
                  aria-label="项目范围"
                  placeholder="项目范围"
                  size="small"
                  clearable
                  :aria-invalid="!!getRangeFieldError(index, 'projectRange')"
                  :aria-describedby="
                    getRangeFieldError(index, 'projectRange')
                      ? getRangeFieldErrorId(index, 'projectRange')
                      : undefined
                  "
                  @input="draft.rangeError = ''"
                  @blur="normalizeRangeInput(draft, 'projectRange')"
                />
                <span
                  v-if="getRangeFieldError(index, 'projectRange')"
                  :id="getRangeFieldErrorId(index, 'projectRange')"
                  class="range-field-error"
                  role="alert"
                >
                  {{ getRangeFieldError(index, "projectRange") }}
                </span>
              </div>
              <div class="excel-region-field">
                <el-input
                  v-model="draft.specificationRange"
                  aria-label="规格范围"
                  placeholder="规格范围"
                  size="small"
                  clearable
                  :aria-invalid="
                    !!getRangeFieldError(index, 'specificationRange')
                  "
                  :aria-describedby="
                    getRangeFieldError(index, 'specificationRange')
                      ? getRangeFieldErrorId(index, 'specificationRange')
                      : undefined
                  "
                  @input="draft.rangeError = ''"
                  @blur="normalizeRangeInput(draft, 'specificationRange')"
                />
                <span
                  v-if="getRangeFieldError(index, 'specificationRange')"
                  :id="getRangeFieldErrorId(index, 'specificationRange')"
                  class="range-field-error"
                  role="alert"
                >
                  {{ getRangeFieldError(index, "specificationRange") }}
                </span>
              </div>
              <div class="excel-region-field">
                <el-input
                  v-model="draft.acceptanceRange"
                  aria-label="验收范围"
                  placeholder="验收范围"
                  size="small"
                  clearable
                  :aria-invalid="!!getRangeFieldError(index, 'acceptanceRange')"
                  :aria-describedby="
                    getRangeFieldError(index, 'acceptanceRange')
                      ? getRangeFieldErrorId(index, 'acceptanceRange')
                      : undefined
                  "
                  @input="draft.rangeError = ''"
                  @blur="normalizeRangeInput(draft, 'acceptanceRange')"
                />
                <span
                  v-if="getRangeFieldError(index, 'acceptanceRange')"
                  :id="getRangeFieldErrorId(index, 'acceptanceRange')"
                  class="range-field-error"
                  role="alert"
                >
                  {{ getRangeFieldError(index, "acceptanceRange") }}
                </span>
              </div>
              <div class="excel-region-field">
                <el-input
                  v-model="draft.remarkRange"
                  aria-label="备注范围"
                  placeholder="备注范围（可选）"
                  size="small"
                  clearable
                  :aria-invalid="!!getRangeFieldError(index, 'remarkRange')"
                  :aria-describedby="
                    getRangeFieldError(index, 'remarkRange')
                      ? getRangeFieldErrorId(index, 'remarkRange')
                      : undefined
                  "
                  @input="draft.rangeError = ''"
                  @blur="normalizeRangeInput(draft, 'remarkRange')"
                />
                <span
                  v-if="getRangeFieldError(index, 'remarkRange')"
                  :id="getRangeFieldErrorId(index, 'remarkRange')"
                  class="range-field-error"
                  role="alert"
                >
                  {{ getRangeFieldError(index, "remarkRange") }}
                </span>
              </div>
            </div>
            <el-button
              class="excel-region-delete"
              type="danger"
              link
              :disabled="drafts.length <= 1"
              @click="removeRegion(index)"
            >
              删除
            </el-button>
          </div>
          <p v-if="draft.rangeError" class="range-error" role="alert">
            {{ draft.rangeError }}
          </p>
        </template>

        <template v-else>
          <div class="row-editor-grid">
            <label>
              <span>表头起始行</span>
              <el-input-number
                v-model="draft.headerStartRow"
                :min="baseRow"
                :max="maximumRow"
                controls-position="right"
              />
            </label>
            <label>
              <span>表头结束行</span>
              <el-input-number
                v-model="draft.headerEndRow"
                :min="draft.headerStartRow"
                :max="maximumRow"
                controls-position="right"
              />
            </label>
            <label>
              <span>数据起始行</span>
              <el-input-number
                v-model="draft.dataStartRow"
                :min="draft.headerEndRow + 1"
                :max="maximumRow"
                controls-position="right"
              />
            </label>
            <label>
              <span>数据结束行</span>
              <el-input-number
                v-model="draft.dataEndRow"
                :min="draft.dataStartRow"
                :max="maximumRow"
                controls-position="right"
              />
            </label>
          </div>

          <div class="column-editor-grid">
            <label>
              <span>项目列</span>
              <el-select
                v-model="draft.projectColumnIndex"
                :disabled="draft.isSpecificationOnly || draft.headersLoading"
                :loading="draft.headersLoading"
                clearable
                placeholder="请选择"
              >
                <el-option
                  v-for="option in getColumnOptions(draft)"
                  :key="option.value"
                  :value="option.value"
                  :label="`[${option.column}] ${option.label}`"
                />
              </el-select>
            </label>
            <label>
              <span>规格列</span>
              <el-select
                v-model="draft.specificationColumnIndex"
                :disabled="draft.headersLoading"
                :loading="draft.headersLoading"
                clearable
                placeholder="请选择"
              >
                <el-option
                  v-for="option in getColumnOptions(draft)"
                  :key="option.value"
                  :value="option.value"
                  :label="`[${option.column}] ${option.label}`"
                />
              </el-select>
            </label>
            <label>
              <span>验收列</span>
              <el-select
                v-model="draft.acceptanceColumnIndex"
                :disabled="draft.headersLoading"
                :loading="draft.headersLoading"
                clearable
                placeholder="请选择"
              >
                <el-option
                  v-for="option in getColumnOptions(draft)"
                  :key="option.value"
                  :value="option.value"
                  :label="`[${option.column}] ${option.label}`"
                />
              </el-select>
            </label>
            <label>
              <span>备注列（可选）</span>
              <el-select
                v-model="draft.remarkColumnIndex"
                :disabled="draft.headersLoading"
                :loading="draft.headersLoading"
                clearable
                placeholder="未设置"
              >
                <el-option
                  v-for="option in getColumnOptions(draft)"
                  :key="option.value"
                  :value="option.value"
                  :label="`[${option.column}] ${option.label}`"
                />
              </el-select>
            </label>
          </div>
        </template>

        <div v-if="!isExcelFile" class="region-editor-foot">
          <span>仅规格表（没有项目列）</span>
          <el-switch
            v-model="draft.isSpecificationOnly"
            :aria-label="'区域 ' + (index + 1) + ' 是否仅规格表'"
          />
        </div>
      </section>

      <button class="add-region-button" type="button" @click="addRegion">
        ＋ 添加数据区域
      </button>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <p
          v-if="isExcelFile && liveExcelValidationError"
          class="drawer-validation-summary"
          role="alert"
        >
          {{ liveExcelValidationError }}
        </p>
        <div class="drawer-actions">
          <el-button @click="visible = false">取消</el-button>
          <el-button
            type="primary"
            :loading="saving"
            :disabled="saveDisabled"
            :title="
              liveExcelValidationError ||
              (saving ? '正在保存范围' : '保存当前范围')
            "
            @click="saveRanges"
          >
            保存范围
          </el-button>
        </div>
      </div>
    </template>
  </el-drawer>
</template>

<style scoped>
:global(.smart-structure-range-drawer .el-drawer__header) {
  padding: 10px 16px;
  margin-bottom: 0;
}

:global(.smart-structure-range-drawer .el-drawer__body) {
  padding-top: 8px;
}

.range-editor-intro {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 12px 14px;
  margin-bottom: 14px;
  color: var(--app-text-primary);
  background: var(--app-info-bg);
  border-left: 3px solid var(--app-primary);
  border-radius: 4px 8px 8px 4px;
}

.range-editor-intro span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.region-editor-list {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.region-editor-card {
  padding: 14px;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: 10px;
  box-shadow: 0 4px 16px rgb(15 43 77 / 5%);
}

.region-editor-card--excel {
  padding: 9px 10px;
  border-radius: 6px;
  box-shadow: none;
}

.region-editor-header,
.region-editor-header > div,
.region-editor-foot,
.drawer-actions {
  display: flex;
  gap: 9px;
  align-items: center;
}

.region-editor-header {
  justify-content: space-between;
  padding-bottom: 12px;
  margin-bottom: 12px;
  border-bottom: 1px solid var(--app-border);
}

.region-editor-header > div {
  min-width: 0;
}

.region-number {
  display: grid;
  place-items: center;
  width: 24px;
  height: 24px;
  font-size: 12px;
  font-weight: 800;
  color: #fff;
  background: var(--app-primary);
  border-radius: 50%;
}

.region-editor-header code {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  color: var(--app-primary);
  white-space: nowrap;
}

.row-editor-grid,
.column-editor-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 12px;
}

.column-editor-grid {
  margin-top: 12px;
}

.row-editor-grid label,
.column-editor-grid label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.row-editor-grid :deep(.el-input-number),
.column-editor-grid :deep(.el-select) {
  width: 100%;
}

.excel-region-row {
  display: grid;
  grid-template-columns: 28px repeat(4, minmax(0, 1fr)) auto;
  gap: 8px;
  align-items: start;
}

.excel-region-index {
  display: grid;
  place-items: center;
  width: 22px;
  height: 22px;
  margin-top: 3px;
  font-size: 12px;
  font-weight: 700;
  color: var(--app-primary);
  border: 1px solid currentcolor;
  border-radius: 50%;
}

.excel-region-fields {
  display: contents;
}

.excel-region-field {
  min-width: 0;
}

.excel-region-field :deep(.el-input) {
  width: 100%;
}

.excel-region-field :deep(.el-input__wrapper) {
  padding-inline: 7px;
  background: transparent;
  box-shadow: none;
}

.excel-region-field :deep(.el-input__wrapper:hover),
.excel-region-field :deep(.el-input__wrapper.is-focus) {
  background: var(--app-info-bg);
  box-shadow: 0 0 0 1px var(--app-primary) inset;
}

.excel-region-delete {
  min-height: 28px;
  padding-inline: 2px;
}

.range-error {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--el-color-danger);
}

.range-field-error {
  display: block;
  margin-top: 2px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-color-danger);
}

.excel-region-field :deep(.el-input[aria-invalid="true"] .el-input__wrapper) {
  box-shadow: 0 0 0 1px var(--el-color-danger) inset;
}

.region-editor-foot {
  justify-content: flex-end;
  margin-top: 12px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.add-region-button {
  min-height: 46px;
  color: var(--app-primary);
  cursor: pointer;
  background: transparent;
  border: 1px dashed color-mix(in srgb, var(--app-primary) 55%, transparent);
  border-radius: 9px;
  transition:
    background 0.2s ease,
    border-color 0.2s ease;
}

.add-region-button:hover {
  background: var(--app-info-bg);
  border-color: var(--app-primary);
}

.drawer-actions {
  justify-content: flex-end;
}

.drawer-footer {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
}

.drawer-validation-summary {
  min-width: 0;
  margin: 0;
  font-size: 12px;
  color: var(--el-color-danger);
  text-align: left;
}

@media (width <= 720px) {
  .row-editor-grid,
  .column-editor-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .excel-region-row {
    grid-template-columns: 28px repeat(2, minmax(0, 1fr)) auto;
  }

  .excel-region-fields {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    grid-column: 2 / 4;
    gap: 8px;
  }

  .region-editor-header {
    align-items: flex-start;
  }

  .region-editor-header code {
    display: none;
  }

  .drawer-footer {
    flex-direction: column;
    align-items: stretch;
  }
}
</style>
