<script setup lang="ts">
import { computed, reactive, watch } from "vue";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";
import {
  buildSmartConfigConfirmRequest,
  formatSmartStructurePercent,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel
} from "./smart-structure-recognition";

const props = defineProps<{
  table: SmartConfigRecognizedTable;
  customerId?: number;
  confirming?: boolean;
  readonly?: boolean;
}>();

const emit = defineEmits<{
  confirm: [request: SmartConfigConfirmRequest];
  advanced: [table: SmartConfigRecognizedTable];
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
};

watch(() => props.table, resetState, { immediate: true });

const decisionTag = computed(() =>
  getSmartStructureDecisionTag(props.table.decision)
);

const tableTitle = computed(
  () => props.table.tableName || `表格 ${props.table.tableIndex + 1}`
);

const columnOptions = computed(() =>
  props.table.headers.map((header, index) => ({
    value: index,
    label: `[${index}] ${header || `列${index + 1}`}`
  }))
);

const canConfirm = computed(
  () =>
    !props.readonly &&
    !!props.customerId &&
    state.specificationColumnIndex !== undefined &&
    props.table.decision !== "Reject"
);

const emitConfirm = () => {
  if (!props.customerId || state.specificationColumnIndex === undefined) {
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
    })
  );
};
</script>

<template>
  <section class="smart-structure-card">
    <div class="card-header">
      <div class="card-title">
        <span>{{ tableTitle }}</span>
        <el-tag size="small" :type="decisionTag.type" effect="plain">
          {{ decisionTag.text }}
        </el-tag>
      </div>
      <div class="card-meta">
        <span>{{ table.source || "-" }}</span>
        <span>置信度 {{ formatSmartStructurePercent(table.confidence) }}</span>
      </div>
    </div>

    <div class="headers-preview">
      <span class="headers-label">表头</span>
      <el-tag
        v-for="(header, index) in table.headers.slice(0, 10)"
        :key="`${table.tableIndex}-${index}`"
        size="small"
        type="info"
        effect="plain"
      >
        [{{ index }}] {{ header || `列${index + 1}` }}
      </el-tag>
      <span v-if="table.headers.length > 10" class="more">...</span>
    </div>

    <el-form label-width="96px" size="small" class="confirm-form">
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
          <el-form-item label="项目列">
            <el-select
              v-model="state.projectColumnIndex"
              :disabled="readonly || state.isSpecificationOnly"
              placeholder="可选"
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
          <el-form-item label="验收列">
            <el-select
              v-model="state.acceptanceColumnIndex"
              :disabled="readonly"
              placeholder="可选"
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
              placeholder="可选"
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
              v-model="state.headerRowIndex"
              :disabled="readonly"
              :min="0"
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
              v-model="state.dataStartRowIndex"
              :disabled="readonly"
              :min="0"
            />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <div v-if="table.fields.length > 0" class="field-list">
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
  padding: 14px 16px;
  background: #fff;
  border: 1px solid #dce4ee;
  border-radius: 8px;
}

.card-header {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 12px;
}

.card-title {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  min-width: 0;
  font-weight: 700;
  color: #1f3349;
}

.card-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 12px;
  justify-content: flex-end;
  font-size: 12px;
  color: #6b7785;
}

.headers-preview,
.field-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  margin-bottom: 12px;
}

.headers-label,
.more {
  font-size: 12px;
  color: #808b98;
}

.confirm-form {
  padding: 10px 12px 0;
  margin-bottom: 12px;
  background: #fbfcfd;
  border: 1px solid #e5ebf2;
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
