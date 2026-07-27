<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getExecutionHistoryDetail,
  getExecutionHistoryList,
  type ExecutionHistoryDetail,
  type ExecutionHistoryListItem
} from "@/api/execution-history";
import ExecutionHistoryBatchReplyDetail from "./components/ExecutionHistoryBatchReplyDetail.vue";
import ExecutionHistorySmartFillPlayback from "./components/ExecutionHistorySmartFillPlayback.vue";
import { formatExecutionHistoryDateTime } from "./executionHistory.formatters";
import { createExecutionHistoryRequestGate } from "./useExecutionHistoryRequests";

defineOptions({
  name: "ExecutionHistory"
});

const loading = ref(false);
const detailLoading = ref(false);
const taskOptions = ref<ExecutionHistoryListItem[]>([]);
const total = ref(0);
const selectedTaskId = ref<number | undefined>(undefined);
const currentDetail = ref<ExecutionHistoryDetail | null>(null);
const listGate = createExecutionHistoryRequestGate();
const detailGate = createExecutionHistoryRequestGate();

const queryParams = reactive({
  keyword: "",
  taskType: ""
});
const pagination = reactive({
  page: 1,
  pageSize: 50
});

const taskTypeOptions = [
  { label: "全部", value: "" },
  { label: "智能填充", value: "smart-fill" },
  { label: "批量回复", value: "batch-reply" }
];

const isSmartFillTask = computed(
  () => currentDetail.value?.taskType === "smart-fill"
);

const taskTypeText = (taskType: string) =>
  taskType === "batch-reply" ? "批量回复" : "智能填充";

const formatTaskOptionLabel = (item: ExecutionHistoryListItem) => {
  const summary =
    item.taskType === "smart-fill" && item.smartFillSummary
      ? `完全匹配 ${item.smartFillSummary.exactMatchedRowCount ?? "-"} / AI ${item.smartFillSummary.aiMatchedRowCount ?? "-"} / 未采用/未匹配 ${item.smartFillSummary.notUsedRowCount ?? "-"}`
      : `已采用 ${item.adoptedRowCount} / 未匹配 ${item.unmatchedRowCount}`;

  return `${taskTypeText(item.taskType)}｜${item.sourceFileName}｜${formatExecutionHistoryDateTime(item.createdAt)}｜${summary}`;
};

const loadDetailById = async (id: number) => {
  const request = detailGate.begin(`detail:${id}`);
  selectedTaskId.value = id;
  currentDetail.value = null;
  detailLoading.value = true;
  try {
    const res = await getExecutionHistoryDetail(id, request.signal);
    if (!request.isCurrent() || selectedTaskId.value !== id) return;

    if (res.code === 0) {
      currentDetail.value = res.data;
      return;
    }

    ElMessage.error(res.message || "加载任务详情失败");
  } catch {
    if (request.isCurrent()) {
      ElMessage.error("加载任务详情失败");
    }
  } finally {
    if (request.isCurrent()) {
      detailLoading.value = false;
    }
  }
};

const loadList = async () => {
  const request = listGate.begin(
    `list:${pagination.page}:${pagination.pageSize}:${queryParams.keyword}:${queryParams.taskType}`
  );
  detailGate.cancel();
  detailLoading.value = false;
  loading.value = true;
  try {
    const res = await getExecutionHistoryList(
      {
        page: pagination.page,
        pageSize: pagination.pageSize,
        keyword: queryParams.keyword || undefined,
        taskType: queryParams.taskType || undefined
      },
      request.signal
    );
    if (!request.isCurrent()) return;

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
    if (request.isCurrent()) {
      ElMessage.error("加载执行记录失败");
    }
  } finally {
    if (request.isCurrent()) {
      loading.value = false;
    }
  }
};

const handleSearch = () => {
  pagination.page = 1;
  void loadList();
};

const handleReset = () => {
  queryParams.keyword = "";
  queryParams.taskType = "";
  pagination.page = 1;
  void loadList();
};

const handleTaskChange = (id?: number) => {
  if (!id) return;
  void loadDetailById(id);
};

const handlePageChange = (page: number) => {
  pagination.page = page;
  void loadList();
};

const handlePageSizeChange = (pageSize: number) => {
  pagination.page = 1;
  pagination.pageSize = pageSize;
  void loadList();
};

onMounted(() => {
  void loadList();
});

onBeforeUnmount(() => {
  listGate.cancel();
  detailGate.cancel();
});
</script>

<template>
  <div class="page page--fill execution-history-page">
    <el-card v-loading="loading" class="task-card">
      <div class="task-control-row">
        <span class="card-header-tip">共 {{ total }} 条记录</span>
        <el-form :inline="true" class="filter-form task-filter-form">
          <el-form-item label="任务类型">
            <el-select
              v-model="queryParams.taskType"
              class="search-select search-select--200"
            >
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
        <el-select
          v-model="selectedTaskId"
          aria-label="任务下拉"
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
        <el-pagination
          :current-page="pagination.page"
          :page-size="pagination.pageSize"
          :page-sizes="[20, 50, 100, 200]"
          :total="total"
          background
          layout="sizes, prev, pager, next"
          @current-change="handlePageChange"
          @size-change="handlePageSizeChange"
        />
      </div>
    </el-card>

    <el-card v-loading="detailLoading" class="detail-card">
      <template v-if="currentDetail">
        <ExecutionHistorySmartFillPlayback
          v-if="isSmartFillTask"
          :detail="currentDetail"
        />

        <ExecutionHistoryBatchReplyDetail
          v-else
          :files="currentDetail.batchReplyDetail?.files ?? currentDetail.files"
        />
      </template>

      <el-empty v-else description="暂无执行记录结果" />
    </el-card>
  </div>
</template>

<style scoped>
.task-card,
.detail-card {
  margin-top: 0;
}

.card-header-tip {
  flex-shrink: 0;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.detail-card {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.detail-card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  height: 100%;
  min-height: 0;
  overflow: hidden;
}

.task-control-row {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
}

.task-select {
  flex: 1 1 520px;
  min-width: 320px;
}

.task-filter-form {
  flex: 0 1 auto;
}
</style>
