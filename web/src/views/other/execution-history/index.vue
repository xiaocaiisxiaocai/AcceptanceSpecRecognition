<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getExecutionHistoryDetail,
  getExecutionHistoryList,
  type ExecutionHistoryDetail,
  type ExecutionHistoryListItem,
  type ExecutionHistorySmartFillSummary
} from "@/api/execution-history";
import ExecutionHistoryBatchReplyDetail from "./components/ExecutionHistoryBatchReplyDetail.vue";
import ExecutionHistorySmartFillPlayback from "./components/ExecutionHistorySmartFillPlayback.vue";

defineOptions({
  name: "ExecutionHistory"
});

const loading = ref(false);
const detailLoading = ref(false);
const taskOptions = ref<ExecutionHistoryListItem[]>([]);
const total = ref(0);
const selectedTaskId = ref<number | undefined>(undefined);
const currentDetail = ref<ExecutionHistoryDetail | null>(null);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: "",
  taskType: ""
});

const taskTypeOptions = [
  { label: "全部", value: "" },
  { label: "智能填充", value: "smart-fill" },
  { label: "批量回复", value: "batch-reply" }
];

const currentTask = computed(
  () => taskOptions.value.find(item => item.id === selectedTaskId.value) ?? null
);

const isSmartFillTask = computed(
  () => currentDetail.value?.taskType === "smart-fill"
);

const isLegacySmartFill = computed(
  () =>
    isSmartFillTask.value &&
    currentDetail.value?.smartFillPlayback?.isLegacy === true
);

const currentSmartFillSummary = computed<ExecutionHistorySmartFillSummary | undefined>(
  () => currentDetail.value?.smartFillSummary ?? currentTask.value?.smartFillSummary
);

const taskTypeText = (taskType: string) =>
  taskType === "batch-reply" ? "批量回复" : "智能填充";

const formatDateTime = (value?: string) => {
  if (!value) return "-";
  return new Date(value).toLocaleString();
};

const formatTaskOptionLabel = (item: ExecutionHistoryListItem) => {
  const summary = item.taskType === "smart-fill" && item.smartFillSummary
    ? `完全 ${item.smartFillSummary.exactMatchedRowCount ?? "-"} / AI ${item.smartFillSummary.aiMatchedRowCount ?? "-"} / 未采用或未匹配 ${item.smartFillSummary.notUsedRowCount ?? "-"}`
    : `已采用 ${item.adoptedRowCount} / 未匹配 ${item.unmatchedRowCount}`;

  return `${taskTypeText(item.taskType)}｜${item.sourceFileName}｜${formatDateTime(item.createdAt)}｜${summary}`;
};

const summaryCards = computed(() => {
  if (!currentDetail.value) return [];

  if (isSmartFillTask.value) {
    const summary = currentSmartFillSummary.value;
    return [
      { label: "完全匹配", value: summary?.exactMatchedRowCount ?? "-" },
      { label: "AI匹配", value: summary?.aiMatchedRowCount ?? "-" },
      { label: "人工确认", value: summary?.manualConfirmedRowCount ?? "-" },
      { label: "人工写入", value: summary?.manualEditedRowCount ?? "-" },
      { label: "未采用/未匹配", value: summary?.notUsedRowCount ?? "-" }
    ];
  }

  return [
    { label: "文件", value: currentDetail.value.fileCount },
    { label: "总行数", value: currentDetail.value.totalRowCount },
    { label: "已采用", value: currentDetail.value.adoptedRowCount },
    { label: "未匹配", value: currentDetail.value.unmatchedRowCount },
    { label: "已跳过", value: currentDetail.value.skippedRowCount }
  ];
});

const loadDetailById = async (id: number) => {
  detailLoading.value = true;
  try {
    const res = await getExecutionHistoryDetail(id);
    if (res.code === 0) {
      currentDetail.value = res.data;
      selectedTaskId.value = id;
      return;
    }

    ElMessage.error(res.message || "加载任务详情失败");
  } catch {
    ElMessage.error("加载任务详情失败");
  } finally {
    detailLoading.value = false;
  }
};

const loadList = async () => {
  loading.value = true;
  try {
    const res = await getExecutionHistoryList({
      page: queryParams.page,
      pageSize: queryParams.pageSize,
      keyword: queryParams.keyword || undefined,
      taskType: queryParams.taskType || undefined
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "加载执行记录失败");
      return;
    }

    taskOptions.value = res.data.items;
    total.value = res.data.total;

    if (taskOptions.value.length === 0) {
      selectedTaskId.value = undefined;
      currentDetail.value = null;
      return;
    }

    const retained = selectedTaskId.value
      ? taskOptions.value.find(item => item.id === selectedTaskId.value)
      : null;
    const target = retained ?? taskOptions.value[0];
    await loadDetailById(target.id);
  } catch {
    ElMessage.error("加载执行记录失败");
  } finally {
    loading.value = false;
  }
};

const handleSearch = () => {
  queryParams.page = 1;
  void loadList();
};

const handleReset = () => {
  queryParams.page = 1;
  queryParams.pageSize = 20;
  queryParams.keyword = "";
  queryParams.taskType = "";
  void loadList();
};

const handleTaskChange = (id?: number) => {
  if (!id) return;
  void loadDetailById(id);
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  void loadList();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  void loadList();
};

onMounted(() => {
  void loadList();
});
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">执行记录</div>
        <div class="page-subtitle">
          按任务回看智能填充与批量回复结果，智能填充支持完整回放，批量回复保持简化详情
        </div>
      </div>
    </div>

    <el-card class="toolbar-card">
      <el-form :inline="true">
        <el-form-item label="任务类型">
          <el-select v-model="queryParams.taskType" class="search-select search-select--240">
            <el-option
              v-for="item in taskTypeOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            clearable
            placeholder="任务ID / 来源文件"
            @keyup.enter="handleSearch"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="handleSearch">搜索</el-button>
          <el-button @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card class="task-card" v-loading="loading">
      <template #header>
        <div class="card-header">
          <span>任务下拉</span>
          <span class="card-header-tip">当前页共 {{ total }} 条记录</span>
        </div>
      </template>

      <div class="task-select-block">
        <el-select
          v-model="selectedTaskId"
          class="task-select"
          filterable
          placeholder="请选择任务"
          @change="handleTaskChange"
        >
          <el-option
            v-for="item in taskOptions"
            :key="item.id"
            :label="formatTaskOptionLabel(item)"
            :value="item.id"
          />
        </el-select>

        <div v-if="currentTask" class="task-brief">
          <div class="task-brief__title">{{ currentTask.sourceFileName }}</div>
          <div class="task-brief__meta">
            <span>{{ taskTypeText(currentTask.taskType) }}</span>
            <span>任务ID {{ currentTask.taskId }}</span>
            <span>{{ formatDateTime(currentTask.createdAt) }}</span>
          </div>
        </div>
      </div>

      <div class="pager-wrap">
        <el-pagination
          background
          layout="total, sizes, prev, pager, next"
          :total="total"
          :page-size="queryParams.pageSize"
          :current-page="queryParams.page"
          @current-change="handlePageChange"
          @size-change="handleSizeChange"
        />
      </div>
    </el-card>

    <el-card class="detail-card" v-loading="detailLoading">
      <template #header>
        <div class="card-header">
          <span>任务详情</span>
          <span v-if="currentDetail" class="card-header-tip">
            {{ taskTypeText(currentDetail.taskType) }} / {{ currentDetail.sourceFileName }}
          </span>
        </div>
      </template>

      <template v-if="currentDetail">
        <div class="summary-grid">
          <div
            v-for="item in summaryCards"
            :key="item.label"
            class="summary-card"
          >
            <div class="summary-card__label">{{ item.label }}</div>
            <div class="summary-card__value">{{ item.value }}</div>
          </div>
        </div>

        <el-alert
          v-if="isLegacySmartFill"
          type="warning"
          :closable="false"
          class="legacy-alert"
          :title="currentDetail.smartFillPlayback?.legacyMessage || '历史记录，缺少预览归档'"
        />

        <ExecutionHistorySmartFillPlayback
          v-if="isSmartFillTask && !isLegacySmartFill"
          :detail="currentDetail"
        />

        <ExecutionHistoryBatchReplyDetail
          v-else
          :files="currentDetail.batchReplyDetail?.files ?? currentDetail.files"
        />
      </template>

      <el-empty v-else description="暂无执行记录详情" />
    </el-card>
  </div>
</template>

<style scoped>
.task-card,
.detail-card {
  margin-top: 16px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.card-header-tip {
  font-size: 12px;
  color: #6b7280;
}

.task-select-block {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.task-select {
  width: 100%;
}

.task-brief {
  padding: 14px 16px;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  background: linear-gradient(180deg, #ffffff 0%, #f8fafc 100%);
}

.task-brief__title {
  font-size: 15px;
  font-weight: 600;
  color: #111827;
}

.task-brief__meta {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 6px;
  font-size: 12px;
  color: #6b7280;
}

.pager-wrap {
  display: flex;
  justify-content: flex-end;
  margin-top: 16px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(5, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 16px;
}

.summary-card {
  padding: 14px 16px;
  border: 1px solid #e5e7eb;
  border-radius: 16px;
  background: linear-gradient(180deg, #ffffff 0%, #f8fafc 100%);
}

.summary-card__label {
  font-size: 12px;
  color: #6b7280;
}

.summary-card__value {
  margin-top: 8px;
  font-size: 24px;
  font-weight: 700;
  color: #111827;
}

.legacy-alert {
  margin-bottom: 16px;
}

@media (max-width: 1200px) {
  .summary-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
}

@media (max-width: 768px) {
  .summary-grid {
    grid-template-columns: 1fr;
  }
}
</style>
