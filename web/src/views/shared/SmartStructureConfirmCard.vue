<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
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
  toDisplayIndexFromZeroBased,
  toZeroBasedIndexFromDisplay
} from "./smart-structure-recognition";

const props = defineProps<{
  table: SmartConfigRecognizedTable;
  customerId?: number;
  confirming?: boolean;
  readonly?: boolean;
  defaultExpanded?: boolean;
  importSelected?: boolean;
  importSelectable?: boolean;
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
const detailVisible = ref(false);

const resetState = () => {
  state.templateName =
    props.table.tableName?.trim() || `表格 ${props.table.tableIndex + 1}`;
  state.projectColumnIndex = props.table.projectColumnIndex;
  state.specificationColumnIndex = props.table.specificationColumnIndex;
  state.acceptanceColumnIndex = props.table.acceptanceColumnIndex;
  state.remarkColumnIndex = props.table.remarkColumnIndex;
  state.headerRowIndex = props.table.headerRowIndex;
  state.headerRowCount = props.table.headerRowCount;
  state.dataStartRowIndex = props.table.dataStartRowIndex;
  state.dataEndRowIndex = props.table.dataEndRowIndex;
  state.isSpecificationOnly = props.table.isSpecificationOnly;
  detailVisible.value =
    props.defaultExpanded ??
    (props.table.recommendation !== "Skip" &&
      props.table.decision !== "AutoApply");
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
  props.table.headers.map((header, index) => ({
    value: index,
    label: `[${formatDisplayIndexFromZeroBased(index)}] ${
      header || `列${index + 1}`
    }`
  }))
);

const getHeaderText = (index?: number) =>
  index === undefined ? "-" : props.table.headers[index] || `列${index + 1}`;

const summaryFields = computed(() => [
  { label: "项目", value: getHeaderText(props.table.projectColumnIndex) },
  { label: "规格", value: getHeaderText(props.table.specificationColumnIndex) },
  { label: "验收", value: getHeaderText(props.table.acceptanceColumnIndex) },
  { label: "备注", value: getHeaderText(props.table.remarkColumnIndex) }
]);

const hasRequiredProjectColumn = computed(
  () => state.isSpecificationOnly || state.projectColumnIndex !== undefined
);

const canConfirm = computed(
  () =>
    !props.readonly &&
    !!props.customerId &&
    hasRequiredProjectColumn.value &&
    state.specificationColumnIndex !== undefined &&
    state.acceptanceColumnIndex !== undefined &&
    props.table.decision !== "Reject"
);

const importSwitchText = computed(() =>
  props.importSelected ? "参与导入" : "不导入"
);

const visibleIssues = computed(() => props.table.issues?.slice(0, 4) ?? []);

const hasStructureChanges = computed(
  () =>
    state.projectColumnIndex !== props.table.projectColumnIndex ||
    state.specificationColumnIndex !== props.table.specificationColumnIndex ||
    state.acceptanceColumnIndex !== props.table.acceptanceColumnIndex ||
    state.remarkColumnIndex !== props.table.remarkColumnIndex ||
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

const displayDataStartRowIndex = computed({
  get: () => toDisplayIndexFromZeroBased(state.dataStartRowIndex),
  set: value => {
    state.dataStartRowIndex = toZeroBasedIndexFromDisplay(value);
  }
});

const displayRowRangeText = computed(() =>
  formatDisplayRowRange({
    headerRowIndex: state.headerRowIndex,
    dataStartRowIndex: state.dataStartRowIndex
  })
);

const emitConfirm = () => {
  if (
    !props.customerId ||
    !hasRequiredProjectColumn.value ||
    state.specificationColumnIndex === undefined ||
    state.acceptanceColumnIndex === undefined
  ) {
    return;
  }

  emit(
    "confirm",
    buildSmartConfigConfirmRequest(props.customerId, {
      ...props.table,
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
      userModifiedStructure: hasStructureChanges.value
    })
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
      <div class="card-meta">
        <span>{{ table.source || "-" }}</span>
        <span>置信度 {{ formatSmartStructurePercent(table.confidence) }}</span>
        <span>
          排序分 {{ formatSmartStructurePercent(table.rankingScore) }}
        </span>
      </div>
    </div>

    <div class="card-summary-strip">
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

    <div v-show="detailVisible" class="headers-preview">
      <span class="headers-label">表头</span>
      <el-tag
        v-for="(header, index) in table.headers.slice(0, 10)"
        :key="`${table.tableIndex}-${index}`"
        size="small"
        type="info"
        effect="plain"
      >
        [{{ formatDisplayIndexFromZeroBased(index) }}]
        {{ header || `列${index + 1}` }}
      </el-tag>
      <span v-if="table.headers.length > 10" class="more">...</span>
    </div>

    <el-form
      v-show="detailVisible"
      label-width="96px"
      size="small"
      class="confirm-form"
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
          <el-form-item label="规格列" required>
            <el-select
              v-model="state.specificationColumnIndex"
              :disabled="readonly"
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
          <el-form-item label="项目列" :required="!state.isSpecificationOnly">
            <el-select
              v-model="state.projectColumnIndex"
              :disabled="readonly || state.isSpecificationOnly"
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
          <el-form-item label="验收列" required>
            <el-select
              v-model="state.acceptanceColumnIndex"
              :disabled="readonly"
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
              :disabled="readonly"
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
              :min="1"
            />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <div
      v-show="detailVisible"
      v-if="table.fields?.length > 0"
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
      <el-button type="primary" link @click="emit('advanced', table)">
        高级手动配置
      </el-button>
      <el-button
        type="primary"
        :disabled="!canConfirm"
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
.issue-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  margin-top: 10px;
  margin-bottom: 10px;
}

.headers-label,
.more {
  font-size: 12px;
  color: var(--app-text-disabled);
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

@media (max-width: 768px) {
  .card-header,
  .card-actions {
    align-items: stretch;
    flex-direction: column;
  }

  .card-meta {
    justify-content: flex-start;
  }
}
</style>
