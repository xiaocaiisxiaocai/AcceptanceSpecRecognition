<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { getTablePreview } from "@/api/document";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import {
  buildSmartConfigConfirmRequest,
  formatDisplayIndexFromZeroBased,
  formatDisplayRowRange,
  formatSmartStructurePercent,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel,
  getSmartStructureIssueTagType,
  getSmartStructureRecommendationTag,
  getSmartStructureTableKindLabel,
  needsManualStructureFallback,
  toDisplayIndexFromZeroBased,
  toZeroBasedIndexFromDisplay
} from "./smart-structure-recognition";
import { requiredSelectionRule, validateForm } from "@/utils/form-rules";

const props = defineProps<{
  table: SmartConfigRecognizedTable;
  fileId?: number;
  customerId?: number;
  confirming?: boolean;
  readonly?: boolean;
  defaultExpanded?: boolean;
  importSelected?: boolean;
  importSelectable?: boolean;
  selectionDisabledReason?: string;
  selectionPendingReason?: string;
}>();

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
const currentHeaders = ref<string[]>([]);
const headersLoading = ref(false);
let latestHeaderRequestId = 0;
let resettingState = false;

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
  detailVisible.value =
    props.defaultExpanded ??
    (props.table.recommendation !== "Skip" &&
      props.table.decision !== "AutoApply");
  resettingState = false;
};

watch(() => props.table, resetState, { immediate: true });

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

const columnOptions = computed(() =>
  currentHeaders.value.map((header, index) => ({
    value: index,
    label: `[${formatDisplayIndexFromZeroBased(index)}] ${
      header || `列${index + 1}`
    }`
  }))
);

const getHeaderText = (index?: number | null) =>
  index == null ? "-" : currentHeaders.value[index] || `列${index + 1}`;

const summaryFields = computed(() => [
  { label: "项目", value: getHeaderText(state.projectColumnIndex) },
  { label: "规格", value: getHeaderText(state.specificationColumnIndex) },
  { label: "验收", value: getHeaderText(state.acceptanceColumnIndex) },
  { label: "备注", value: getHeaderText(state.remarkColumnIndex) }
]);

const hasRequiredProjectColumn = computed(
  () => state.isSpecificationOnly || state.projectColumnIndex != null
);

const canConfirm = computed(
  () =>
    !props.readonly &&
    !!props.customerId &&
    hasRequiredProjectColumn.value &&
    state.specificationColumnIndex != null &&
    state.acceptanceColumnIndex != null &&
    props.table.decision !== "Reject"
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

const hasStructureChanges = computed(
  () =>
    (state.projectColumnIndex ?? null) !==
      (props.table.projectColumnIndex ?? null) ||
    (state.specificationColumnIndex ?? null) !==
      (props.table.specificationColumnIndex ?? null) ||
    (state.acceptanceColumnIndex ?? null) !==
      (props.table.acceptanceColumnIndex ?? null) ||
    (state.remarkColumnIndex ?? null) !==
      (props.table.remarkColumnIndex ?? null) ||
    state.headerRowIndex !== props.table.headerRowIndex ||
    state.headerRowCount !== props.table.headerRowCount ||
    state.dataStartRowIndex !== props.table.dataStartRowIndex ||
    state.dataEndRowIndex !== props.table.dataEndRowIndex ||
    state.isSpecificationOnly !== props.table.isSpecificationOnly
);

const displayHeaderRowIndex = computed({
  get: () => toDisplayIndexFromZeroBased(state.headerRowIndex),
  set: value => {
    state.headerRowIndex = toZeroBasedIndexFromDisplay(value);
  }
});

const minimumDataStartRowIndex = computed(
  () => state.headerRowIndex + Math.max(state.headerRowCount, 1)
);
const displayMinimumDataStartRowIndex = computed(() =>
  toDisplayIndexFromZeroBased(minimumDataStartRowIndex.value)
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
  get: () => toDisplayIndexFromZeroBased(state.dataStartRowIndex),
  set: value => {
    state.dataStartRowIndex = Math.max(
      toZeroBasedIndexFromDisplay(value),
      minimumDataStartRowIndex.value
    );
  }
});

const displayRowRangeText = computed(() =>
  formatDisplayRowRange({
    headerRowIndex: state.headerRowIndex,
    dataStartRowIndex: state.dataStartRowIndex
  })
);

const emitConfirm = async () => {
  if (!(await validateForm(formRef.value))) return;

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
        isSpecificationOnly: state.isSpecificationOnly
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
          :disabled="readonly || importSelectable === false"
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

    <div v-show="!detailVisible" class="card-summary-strip">
      <el-tag
        v-for="field in summaryFields"
        :key="field.label"
        size="small"
        effect="plain"
        :type="field.value === '-' ? 'info' : 'primary'"
      >
        {{ field.label }}: {{ field.value }}
      </el-tag>
      <span class="row-range">{{ displayRowRangeText }}</span>
    </div>

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
        [{{ formatDisplayIndexFromZeroBased(index) }}]
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
        [{{ formatDisplayIndexFromZeroBased(suggestion.columnIndex) }}]
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
              :disabled="readonly"
              placeholder="确认后保存为客户模板"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="12" :lg="8">
          <el-form-item label="项目列" prop="projectColumnIndex">
            <el-select
              v-model="state.projectColumnIndex"
              :disabled="
                readonly || state.isSpecificationOnly || headersLoading
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
              :disabled="readonly || headersLoading"
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
              :disabled="readonly || headersLoading"
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
              :disabled="readonly || headersLoading"
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
              :disabled="readonly"
              active-text="是"
              inactive-text="否"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="表头行">
            <el-input-number
              v-model="displayHeaderRowIndex"
              :disabled="readonly"
              :min="1"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="表头行数">
            <el-input-number
              v-model="state.headerRowCount"
              :disabled="readonly"
              :min="1"
            />
          </el-form-item>
        </el-col>
        <el-col :xs="24" :sm="8">
          <el-form-item label="数据起始">
            <el-input-number
              v-model="displayDataStartRowIndex"
              :disabled="readonly"
              :min="displayMinimumDataStartRowIndex"
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
        {{ detailVisible ? "收起配置" : "展开配置" }}
      </el-button>
      <el-button
        v-if="showAdvancedFallback"
        type="primary"
        link
        @click="emit('advanced', table)"
      >
        手动处理
      </el-button>
      <el-button
        type="primary"
        :disabled="!canConfirm || headersLoading"
        :loading="confirming"
        @click="emitConfirm"
      >
        确认并学习
      </el-button>
    </div>
  </section>
</template>

<style scoped>
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

.card-summary-strip {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  min-height: 26px;
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
