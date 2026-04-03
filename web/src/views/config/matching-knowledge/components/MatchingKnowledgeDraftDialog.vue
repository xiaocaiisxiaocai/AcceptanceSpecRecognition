<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  AiServicePurpose,
  getAiServiceList,
  type AiServiceConfig
} from "@/api/ai-service";
import {
  generateMatchingKnowledgeDraft,
  type MatchingKnowledgeDraftCategory,
  type MatchingKnowledgeDraftItem,
  type MatchingKnowledgeDraftSpecFilter
} from "@/api/matching-knowledge";
import { getSpecList, type AcceptanceSpec } from "@/api/spec";

interface EditableDraftRow {
  id: number;
  selected: boolean;
  key: string;
  value: string;
  evidenceSnippet: string;
  reason: string;
  status: string;
  statusMessage?: string;
  isManual: boolean;
}

const props = defineProps<{
  visible: boolean;
  category: MatchingKnowledgeDraftCategory;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "import", payload: {
    category: MatchingKnowledgeDraftCategory;
    items: MatchingKnowledgeDraftItem[];
  }): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});

const llmService = ref<AiServiceConfig | null>(null);
const previewLoading = ref(false);
const generating = ref(false);
const includeAllFilteredSpecs = ref(true);
const specPreviewRows = ref<AcceptanceSpec[]>([]);
const specPreviewTotal = ref(0);
const draftRows = ref<EditableDraftRow[]>([]);
const importedRange = ref<string[]>([]);

const previewQuery = reactive({
  page: 1,
  pageSize: 10
});

const filters = reactive<MatchingKnowledgeDraftSpecFilter>({
  customerId: undefined,
  processId: undefined,
  machineModelId: undefined,
  keyword: undefined,
  importedFrom: undefined,
  importedTo: undefined
});

let nextRowId = 1;

const allocateRowId = () => {
  const id = nextRowId;
  nextRowId += 1;
  return id;
};

const categoryMeta = computed(() => {
  switch (props.category) {
    case "entityAliases":
      return {
        title: "实体组",
        description: "从历史验规中抽取可并入实体组的品牌、组织、厂商候选词",
        keyLabel: "候选词",
        valueLabel: "标准实体"
      };
    case "unitAliases":
      return {
        title: "单位组",
        description: "从历史验规中抽取可并入单位组的候选词，不生成倍率或换算系数",
        keyLabel: "候选词",
        valueLabel: "标准单位"
      };
    case "fieldAliases":
      return {
        title: "字段组",
        description: "从历史验规中抽取可并入字段组的业务字段、列名、缩写候选词",
        keyLabel: "候选词",
        valueLabel: "标准字段"
      };
    case "conflictPairs":
      return {
        title: "冲突组",
        description: "从历史验规中抽取明确互斥、不能同时成立的左右对立词",
        keyLabel: "左侧候选",
        valueLabel: "右侧候选"
      };
    default:
      return {
        title: "匹配知识",
        description: "从历史验规中生成匹配知识草稿候选",
        keyLabel: "键",
        valueLabel: "值"
      };
  }
});

const dialogTitle = computed(
  () => `AI 生成候选 - ${categoryMeta.value.title}`
);
const llmServiceLabel = computed(() => {
  if (!llmService.value) {
    return "未指定";
  }

  const model = llmService.value.llmModel?.trim();
  return model
    ? `${llmService.value.name}（${model}）`
    : llmService.value.name;
});
const selectedDraftCount = computed(
  () => draftRows.value.filter(row => row.selected).length
);
const selectedSourceCount = computed(() =>
  includeAllFilteredSpecs.value ? specPreviewTotal.value : 0
);
const draftStatusSummary = computed(() => {
  const ready = draftRows.value.filter(row => row.status === "ready").length;
  const duplicate = draftRows.value.filter(
    row => row.status === "duplicate"
  ).length;
  const conflict = draftRows.value.filter(
    row => row.status === "conflict"
  ).length;

  return { ready, duplicate, conflict };
});

watch(
  () => dialogVisible.value,
  visible => {
    if (visible) {
      void initializeDialog();
      return;
    }

    resetDialogState();
  }
);

const createDraftRow = (
  item?: Partial<MatchingKnowledgeDraftItem> & {
    selected?: boolean;
    isManual?: boolean;
  }
): EditableDraftRow => ({
  id: allocateRowId(),
  selected: item?.selected ?? item?.status === "ready",
  key: item?.key ?? "",
  value: item?.value ?? "",
  evidenceSnippet: item?.evidenceSnippet ?? "",
  reason: item?.reason ?? "",
  status: item?.status ?? "ready",
  statusMessage: item?.statusMessage,
  isManual: item?.isManual ?? false
});

const normalizeFilterPayload = (): MatchingKnowledgeDraftSpecFilter => {
  const [importedFrom, importedTo] = importedRange.value ?? [];
  return {
    customerId: filters.customerId,
    processId: filters.processId,
    machineModelId: filters.machineModelId,
    keyword: filters.keyword?.trim() || undefined,
    importedFrom: importedFrom || undefined,
    importedTo: importedTo || undefined
  };
};

const sortLlmServices = (items: AiServiceConfig[]) => {
  return [...items].sort((left, right) => {
    if (left.priority !== right.priority) {
      return left.priority - right.priority;
    }

    const leftTime = new Date(left.updatedAt || left.createdAt).getTime();
    const rightTime = new Date(right.updatedAt || right.createdAt).getTime();
    return rightTime - leftTime;
  });
};

const isMoonshotService = (item: AiServiceConfig) => {
  const name = item.name?.trim().toLowerCase() || "";
  const endpoint = item.endpoint?.trim().toLowerCase() || "";
  const model = item.llmModel?.trim().toLowerCase() || "";

  return (
    name.includes("月之暗面") ||
    name.includes("moonshot") ||
    name.includes("kimi") ||
    endpoint.includes("moonshot.cn") ||
    model.includes("kimi")
  );
};

const loadLlmService = async () => {
  try {
    const res = await getAiServiceList({ page: 1, pageSize: 100 });
    if (res.code !== 0) {
      ElMessage.warning(res.message || "加载 LLM 服务失败，生成时将按后端默认策略处理");
      llmService.value = null;
      return;
    }

    const llmCandidates = sortLlmServices(
      res.data.items.filter(
        item =>
          (item.purpose & AiServicePurpose.Llm) === AiServicePurpose.Llm &&
          !!item.llmModel?.trim()
      )
    );

    llmService.value =
      llmCandidates.find(item => isMoonshotService(item)) ??
      llmCandidates[0] ??
      null;
  } catch {
    llmService.value = null;
    ElMessage.warning("加载 LLM 服务失败，生成时将按后端默认策略处理");
  }
};

const loadSpecPreview = async (resetPage = false) => {
  if (resetPage) {
    previewQuery.page = 1;
  }

  previewLoading.value = true;
  try {
    const res = await getSpecList({
      ...normalizeFilterPayload(),
      page: previewQuery.page,
      pageSize: previewQuery.pageSize
    });

    if (res.code === 0) {
      specPreviewRows.value = res.data.items;
      specPreviewTotal.value = res.data.total;
      return;
    }

    ElMessage.error(res.message || "加载历史验规预览失败");
  } catch {
    ElMessage.error("加载历史验规预览失败");
  } finally {
    previewLoading.value = false;
  }
};

const initializeDialog = async () => {
  includeAllFilteredSpecs.value = true;
  await loadLlmService();
  await loadSpecPreview(true);
};

const resetDialogState = () => {
  filters.customerId = undefined;
  filters.processId = undefined;
  filters.machineModelId = undefined;
  filters.keyword = undefined;
  importedRange.value = [];
  previewQuery.page = 1;
  previewQuery.pageSize = 10;
  includeAllFilteredSpecs.value = true;
  llmService.value = null;
  specPreviewRows.value = [];
  specPreviewTotal.value = 0;
  draftRows.value = [];
  generating.value = false;
};

const handleSearch = async () => {
  await loadSpecPreview(true);
};

const handleReset = async () => {
  filters.customerId = undefined;
  filters.processId = undefined;
  filters.machineModelId = undefined;
  filters.keyword = undefined;
  importedRange.value = [];
  includeAllFilteredSpecs.value = true;
  await loadSpecPreview(true);
};

const handlePreviewPageChange = async (page: number) => {
  previewQuery.page = page;
  await loadSpecPreview();
};

const handlePreviewSizeChange = async (pageSize: number) => {
  previewQuery.pageSize = pageSize;
  previewQuery.page = 1;
  await loadSpecPreview();
};

const getStatusTagType = (status: string) => {
  if (status === "ready") {
    return "success";
  }

  if (status === "duplicate") {
    return "info";
  }

  if (status === "conflict") {
    return "warning";
  }

  return "info";
};

const validateSource = () => {
  if (!includeAllFilteredSpecs.value) {
    ElMessage.warning("当前已取消全选，请先恢复全选后再生成");
    return false;
  }

  if (specPreviewTotal.value === 0) {
    ElMessage.warning("当前筛选条件下没有可用的历史验规");
    return false;
  }

  return true;
};

const handleGenerate = async () => {
  if (!validateSource()) {
    return;
  }

  if (draftRows.value.length > 0) {
    try {
      await ElMessageBox.confirm(
        "重新生成会覆盖当前草稿中的编辑结果，是否继续？",
        "提示",
        {
          type: "warning",
          confirmButtonText: "继续生成",
          cancelButtonText: "取消"
        }
      );
    } catch {
      return;
    }
  }

  generating.value = true;
  try {
    const res = await generateMatchingKnowledgeDraft({
      category: props.category,
      specFilter: normalizeFilterPayload(),
      llmServiceId: llmService.value?.id
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "生成候选失败");
      return;
    }

    draftRows.value = res.data.items.map(item => createDraftRow(item));
    ElMessage.success(
      `已按当前筛选结果生成 ${draftRows.value.length} 条候选，待确认 ${draftStatusSummary.value.ready} 条`
    );
  } catch {
    ElMessage.error("生成候选失败");
  } finally {
    generating.value = false;
  }
};

const addManualRow = () => {
  draftRows.value.unshift(
    createDraftRow({
      key: "",
      value: "",
      evidenceSnippet: "",
      reason: "手动补充",
      status: "ready",
      selected: true,
      isManual: true
    })
  );
};

const removeDraftRow = (id: number) => {
  const index = draftRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    draftRows.value.splice(index, 1);
  }
};

const handleImport = () => {
  const selectedRows = draftRows.value.filter(row => row.selected);
  if (selectedRows.length === 0) {
    ElMessage.warning("请至少选择一条候选再导入");
    return;
  }

  const incomplete = selectedRows.find(
    row => !row.key.trim() || !row.value.trim()
  );
  if (incomplete) {
    ElMessage.warning("存在未填写完整的候选项，请补全后再导入");
    return;
  }

  emit("import", {
    category: props.category,
    items: selectedRows.map(row => ({
      key: row.key.trim(),
      value: row.value.trim(),
      evidenceSnippet: row.evidenceSnippet.trim(),
      reason: row.reason.trim(),
      status: row.status,
      statusMessage: row.statusMessage
    }))
  });
  dialogVisible.value = false;
};

const formatImportedAt = (value?: string) => {
  if (!value) {
    return "-";
  }

  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
};
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    :title="dialogTitle"
    width="1200px"
    destroy-on-close
  >
    <div class="draft-dialog">
      <el-alert
        type="info"
        show-icon
        :closable="false"
        :title="`${categoryMeta.description}。系统会从当前筛选命中的历史验规中生成候选，并且只导入到“自定义扩展”。`"
      />

      <el-card class="source-card" shadow="never">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">历史验规</div>
              <div class="card-subtitle">
                按关键词、导入时间筛选；预览可分页，但生成时始终处理当前筛选命中的全部历史验规
              </div>
              <div class="card-subtitle">当前 LLM：{{ llmServiceLabel }}</div>
            </div>
            <el-button
              type="primary"
              :loading="generating"
              @click="handleGenerate"
            >
              生成当前分类候选
            </el-button>
          </div>
        </template>

        <el-form inline class="filter-form">
          <el-form-item label="导入时间">
            <el-date-picker
              v-model="importedRange"
              type="datetimerange"
              :automatic-dropdown="false"
              unlink-panels
              value-format="YYYY-MM-DDTHH:mm:ss"
              start-placeholder="开始时间"
              end-placeholder="结束时间"
            />
          </el-form-item>

          <el-form-item label="关键词">
            <el-input
              v-model="filters.keyword"
              clearable
              placeholder="项目 / 规格 / 验收 / 备注"
              @keyup.enter="handleSearch"
            />
          </el-form-item>

          <el-form-item>
            <el-button type="primary" @click="handleSearch">搜索</el-button>
            <el-button @click="handleReset">重置</el-button>
          </el-form-item>
        </el-form>

        <div class="selection-bar">
          <div class="helper-text">
            当前筛选结果默认全部参与生成。命中 {{ specPreviewTotal }} 条，当前选择
            {{ selectedSourceCount }} 条。
          </div>
          <div class="selection-actions">
            <el-tag :type="includeAllFilteredSpecs ? 'success' : 'info'" effect="plain">
              {{ includeAllFilteredSpecs ? "已全选" : "已取消" }}
            </el-tag>
            <el-button
              link
              type="primary"
              @click="includeAllFilteredSpecs = true"
            >
              全选
            </el-button>
            <el-button
              link
              type="primary"
              @click="includeAllFilteredSpecs = false"
            >
              取消全选
            </el-button>
          </div>
        </div>

        <el-table
          v-loading="previewLoading"
          :data="specPreviewRows"
          row-key="id"
          border
          max-height="280"
        >
          <el-table-column prop="project" label="项目" min-width="180" show-overflow-tooltip />
          <el-table-column
            prop="specification"
            label="规格内容"
            min-width="220"
            show-overflow-tooltip
          />
          <el-table-column label="导入时间" min-width="180">
            <template #default="{ row }">
              {{ formatImportedAt(row.importedAt) }}
            </template>
          </el-table-column>
        </el-table>

        <div class="pager-wrap">
          <el-pagination
            v-model:current-page="previewQuery.page"
            v-model:page-size="previewQuery.pageSize"
            :page-sizes="[10, 20, 50]"
            :total="specPreviewTotal"
            layout="total, sizes, prev, pager, next"
            @size-change="handlePreviewSizeChange"
            @current-change="handlePreviewPageChange"
          />
        </div>
      </el-card>

      <el-card class="draft-card" shadow="never">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">草稿候选</div>
              <div class="card-subtitle">
                可勾选、编辑、删除或手动新增；导入时只会写入自定义扩展
              </div>
            </div>
            <div class="draft-actions">
              <span class="helper-text">
                已选 {{ selectedDraftCount }} 条，待确认
                {{ draftStatusSummary.ready }} 条，重复
                {{ draftStatusSummary.duplicate }} 条，冲突
                {{ draftStatusSummary.conflict }} 条
              </span>
              <el-button link type="primary" @click="addManualRow">
                新增一条
              </el-button>
            </div>
          </div>
        </template>

        <el-empty
          v-if="draftRows.length === 0"
          description="先筛选历史验规，再点击“生成当前分类候选”，或手动新增草稿条目。"
        />

        <el-table
          v-else
          :data="draftRows"
          row-key="id"
          border
          max-height="360"
        >
          <el-table-column label="导入" width="72" align="center">
            <template #default="{ row }">
              <el-checkbox v-model="row.selected" />
            </template>
          </el-table-column>
          <el-table-column :label="categoryMeta.keyLabel" min-width="180">
            <template #default="{ row }">
              <el-input
                v-model="row.key"
                :placeholder="`输入${categoryMeta.keyLabel}`"
              />
            </template>
          </el-table-column>
          <el-table-column :label="categoryMeta.valueLabel" min-width="180">
            <template #default="{ row }">
              <el-input
                v-model="row.value"
                :placeholder="`输入${categoryMeta.valueLabel}`"
              />
            </template>
          </el-table-column>
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-tag :type="getStatusTagType(row.status)" effect="plain">
                {{ row.status }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            label="状态说明"
            min-width="220"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              {{ row.statusMessage || (row.isManual ? "手动新增候选" : "可直接导入") }}
            </template>
          </el-table-column>
          <el-table-column
            label="证据片段"
            min-width="220"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              {{ row.evidenceSnippet || "-" }}
            </template>
          </el-table-column>
          <el-table-column
            label="生成理由"
            min-width="220"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              {{ row.reason || "-" }}
            </template>
          </el-table-column>
          <el-table-column label="操作" width="90" fixed="right">
            <template #default="{ row }">
              <el-button type="danger" link @click="removeDraftRow(row.id)">
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>

    <template #footer>
      <div class="footer-actions">
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleImport">
          导入到当前配置
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.draft-dialog {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.card-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.card-subtitle {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-secondary);
}

.source-card,
.draft-card {
  border-radius: 16px;
}

.filter-form {
  margin-bottom: 12px;
}

.filter-select {
  width: 180px;
}

.selection-bar,
.selection-actions,
.draft-actions,
.footer-actions,
.pager-wrap {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.selection-bar {
  margin-bottom: 12px;
}

.pager-wrap {
  margin-top: 12px;
  justify-content: flex-end;
}

.helper-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

@media (max-width: 960px) {
  .card-header,
  .selection-bar,
  .selection-actions,
  .draft-actions,
  .footer-actions,
  .pager-wrap {
    flex-direction: column;
    align-items: flex-start;
  }

  .filter-select {
    width: 100%;
  }
}
</style>
