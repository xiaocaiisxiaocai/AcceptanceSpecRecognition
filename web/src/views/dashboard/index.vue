<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { ElMessage } from "element-plus";
import { Refresh } from "@element-plus/icons-vue";
import {
  getDashboardSummary,
  type DashboardPeriodPreset,
  type DashboardSummary,
  type DashboardSummaryRequest
} from "@/api/dashboard";
import {
  getExecutionHistoryList,
  type ExecutionHistoryListItem
} from "@/api/execution-history";
import { formatExecutionHistoryDateTime } from "@/views/other/execution-history/executionHistory.formatters";
import DashboardSparkline from "./components/DashboardSparkline.vue";
import { createDashboardRequestGate } from "./dashboard-request-gate";

defineOptions({
  name: "Dashboard"
});

const emptySummary: DashboardSummary = {
  periodPreset: "last7",
  periodStart: "",
  periodEnd: "",
  customerTotal: 0,
  processTotal: 0,
  specTotal: 0,
  importedSpecCount: 0,
  smartFillTaskCount: 0,
  smartFillTotalRows: 0,
  smartFillMatchedRows: 0,
  smartFillAdoptedRows: 0,
  matchingRate: 0,
  adoptionRate: 0,
  dailyTrend: []
};

const loading = ref(false);
const recentLoading = ref(false);
const periodPreset = ref<DashboardPeriodPreset>("last7");
const customRange = ref<[Date, Date] | null>(null);
const summary = ref<DashboardSummary | null>(null);
const recentTasks = ref<ExecutionHistoryListItem[]>([]);
const trendLabels = ref<string[]>([]);
const importTrend = ref<number[]>([]);
const taskTrend = ref<number[]>([]);
const datePickerDefaultTime: [Date, Date] = [
  new Date(2000, 0, 1, 0, 0, 0),
  new Date(2000, 0, 1, 23, 59, 59)
];

const currentSummary = computed(() => summary.value ?? emptySummary);
const currentPeriodLabel = computed(() => {
  if (currentSummary.value.periodPreset === "last30") return "近 30 天";
  if (currentSummary.value.periodPreset === "custom") return "自定义周期";
  return "近 7 天";
});
const isCanceledRequest = (error: unknown) => {
  if (!(error instanceof Error)) return false;
  return (
    error.name === "AbortError" ||
    error.name === "CanceledError" ||
    (error as Error & { code?: string }).code === "ERR_CANCELED"
  );
};
const periodOptions = [
  { label: "最近7天", value: "last7" },
  { label: "最近30天", value: "last30" },
  { label: "自定义", value: "custom" }
];

const idleScheduler = globalThis as typeof globalThis & {
  requestIdleCallback?: (
    callback: IdleRequestCallback,
    options?: IdleRequestOptions
  ) => number;
  cancelIdleCallback?: (handle: number) => void;
};
let idleCallbackId: number | undefined;
let loadTimerId: ReturnType<typeof setTimeout> | undefined;
const summaryRequestGate = createDashboardRequestGate();
const recentRequestGate = createDashboardRequestGate();

const buildRequest = (): DashboardSummaryRequest => {
  if (periodPreset.value !== "custom") {
    return { range: periodPreset.value };
  }

  if (!customRange.value) {
    return { range: "last7" };
  }

  const [from, to] = customRange.value;
  return {
    range: "custom",
    from: from.toISOString(),
    to: to.toISOString()
  };
};

const load = async () => {
  const ticket = summaryRequestGate.begin();
  loading.value = true;
  try {
    const response = await getDashboardSummary(buildRequest(), ticket.signal);
    if (ticket.isCurrent() && response.code === 0) {
      summary.value = response.data;
      periodPreset.value = response.data.periodPreset;
      trendLabels.value = response.data.dailyTrend.map(item => item.date);
      importTrend.value = response.data.dailyTrend.map(
        item => item.importedSpecCount
      );
      taskTrend.value = response.data.dailyTrend.map(
        item => item.smartFillTaskCount
      );
    } else if (ticket.isCurrent()) {
      ElMessage.error(response.message || "加载首页统计数据失败");
    }
  } catch (error) {
    if (!isCanceledRequest(error)) {
      ElMessage.error("加载首页统计数据失败");
    }
  } finally {
    if (ticket.isCurrent()) {
      loading.value = false;
    }
  }
};

const loadRecentTasks = async () => {
  const ticket = recentRequestGate.begin();
  recentLoading.value = true;
  try {
    const response = await getExecutionHistoryList(
      { page: 1, pageSize: 5 },
      ticket.signal
    );
    if (!ticket.isCurrent()) return;
    if (response.code === 0) {
      recentTasks.value = response.data.items;
      return;
    }

    ElMessage.error(response.message || "加载最近执行记录失败");
  } catch (error) {
    if (!isCanceledRequest(error)) {
      ElMessage.error("加载最近执行记录失败");
    }
  } finally {
    if (ticket.isCurrent()) {
      recentLoading.value = false;
    }
  }
};

const scheduleLoad = () => {
  if (typeof idleScheduler.requestIdleCallback === "function") {
    idleCallbackId = idleScheduler.requestIdleCallback(
      () => {
        void load();
        void loadRecentTasks();
      },
      { timeout: 800 }
    );
    return;
  }

  loadTimerId = globalThis.setTimeout(() => {
    void load();
    void loadRecentTasks();
  }, 120);
};

const ensureDefaultCustomRange = () => {
  if (customRange.value) return;

  const end = new Date();
  const start = new Date();
  start.setDate(end.getDate() - 7);
  start.setHours(0, 0, 0, 0);
  end.setHours(23, 59, 59, 999);
  customRange.value = [start, end];
};

const handlePresetChange = () => {
  if (periodPreset.value === "custom") {
    ensureDefaultCustomRange();
  }

  void load();
};

const handleCustomRangeChange = () => {
  if (periodPreset.value === "custom") {
    void load();
  }
};

const formatNumber = (value: number | undefined) => {
  return (value ?? 0).toLocaleString("zh-CN");
};

const taskTypeText = (taskType: string) =>
  taskType === "batch-reply" ? "批量回复" : "智能填充";

const formatDateTime = (value: string | undefined) => {
  if (!value) return "-";

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";

  return date.toLocaleString("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit"
  });
};

const refreshDashboard = () => {
  void load();
  void loadRecentTasks();
};

onMounted(() => {
  scheduleLoad();
});

onBeforeUnmount(() => {
  if (
    idleCallbackId !== undefined &&
    typeof idleScheduler.cancelIdleCallback === "function"
  ) {
    idleScheduler.cancelIdleCallback(idleCallbackId);
  }

  if (loadTimerId !== undefined) {
    globalThis.clearTimeout(loadTimerId);
  }

  summaryRequestGate.cancel();
  recentRequestGate.cancel();
});
</script>

<template>
  <div class="page dashboard">
    <div class="page-header">
      <div>
        <div class="dashboard-period-range">
          {{ formatDateTime(currentSummary.periodStart) }} 至
          {{ formatDateTime(currentSummary.periodEnd) }}
        </div>
      </div>
      <div class="period-tools">
        <el-segmented
          v-model="periodPreset"
          :options="periodOptions"
          @change="handlePresetChange"
        />
        <el-date-picker
          v-if="periodPreset === 'custom'"
          v-model="customRange"
          type="datetimerange"
          range-separator="至"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          :default-time="datePickerDefaultTime"
          class="period-picker"
          @change="handleCustomRangeChange"
        />
        <el-button :icon="Refresh" :loading="loading" @click="refreshDashboard">
          刷新
        </el-button>
      </div>
    </div>

    <el-row :gutter="16">
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card stat-card--primary">
          <div class="stat stat--progress">
            <div>
              <div class="stat-title">匹配度</div>
              <div class="stat-note">
                已匹配 {{ formatNumber(currentSummary.smartFillMatchedRows) }} /
                {{ formatNumber(currentSummary.smartFillTotalRows) }} 行
              </div>
            </div>
            <el-progress
              type="circle"
              :width="82"
              :stroke-width="8"
              :percentage="Math.round(currentSummary.matchingRate * 100)"
            />
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat stat--progress">
            <div>
              <div class="stat-title">采用率</div>
              <div class="stat-note">
                已采用 {{ formatNumber(currentSummary.smartFillAdoptedRows) }} /
                {{ formatNumber(currentSummary.smartFillMatchedRows) }} 行
              </div>
            </div>
            <el-progress
              type="circle"
              status="success"
              :width="82"
              :stroke-width="8"
              :percentage="Math.round(currentSummary.adoptionRate * 100)"
            />
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card stat-card--import">
          <div class="stat">
            <div class="stat-heading">
              <div>
                <div class="stat-title">历史资料导入</div>
                <div class="stat-value">
                  {{ formatNumber(currentSummary.importedSpecCount) }}
                </div>
              </div>
              <span class="stat-period">{{ currentPeriodLabel }}</span>
            </div>
            <DashboardSparkline
              :values="importTrend"
              :labels="trendLabels"
              :loading="loading"
              color-token="--app-success"
            />
            <div class="stat-note">周期内新增或更新规格</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-heading">
              <div>
                <div class="stat-title">智能填充任务</div>
                <div class="stat-value">
                  {{ formatNumber(currentSummary.smartFillTaskCount) }}
                </div>
              </div>
              <span class="stat-period">{{ currentPeriodLabel }}</span>
            </div>
            <DashboardSparkline
              :values="taskTrend"
              :labels="trendLabels"
              :loading="loading"
            />
            <div class="stat-note">
              规格总量 {{ formatNumber(currentSummary.specTotal) }}
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-card v-loading="recentLoading" class="recent-card">
      <template #header>
        <div class="card-header">
          <span>最近执行</span>
          <span class="card-header-tip">最新 5 条</span>
        </div>
      </template>
      <el-table :data="recentTasks" empty-text="暂无执行记录">
        <el-table-column label="类型" width="92">
          <template #default="{ row }">
            <el-tag size="small" effect="plain">
              {{ taskTypeText(row.taskType) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          prop="sourceFileName"
          label="来源文件"
          min-width="220"
          show-overflow-tooltip
        />
        <el-table-column label="采用/总行" width="110">
          <template #default="{ row }">
            {{ formatNumber(row.adoptedRowCount) }} /
            {{ formatNumber(row.totalRowCount) }}
          </template>
        </el-table-column>
        <el-table-column label="时间" width="160">
          <template #default="{ row }">
            {{ formatExecutionHistoryDateTime(row.createdAt) }}
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 0;
}

.dashboard {
  overflow-x: hidden;
}

.dashboard :deep(.el-row) {
  margin-right: 0 !important;
  margin-left: 0 !important;
}

.page-header {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
}

.dashboard-period-range {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.period-tools {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  align-items: center;
  justify-content: flex-end;
}

.period-picker {
  width: 360px;
}

.stat-card {
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
}

.stat-card--primary {
  background: var(--app-bg-card);
  border-color: color-mix(in srgb, var(--app-primary) 24%, var(--app-border));
}

.stat-card--import {
  background: var(--app-bg-card);
  border-color: color-mix(in srgb, var(--app-success) 24%, var(--app-border));
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 112px;
}

.stat--progress {
  flex-direction: row;
  align-items: center;
  justify-content: space-between;
}

.stat--progress > div:first-child {
  display: flex;
  flex: 1;
  flex-direction: column;
  align-self: stretch;
  justify-content: space-between;
  min-width: 0;
}

.stat-heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
}

.stat-period {
  padding: 3px 7px;
  font-size: 11px;
  color: var(--app-text-secondary);
  background: var(--app-fill-light);
  border-radius: 999px;
}

.stat-title {
  font-size: 14px;
  color: var(--app-text-secondary);
}

.stat-value {
  font-size: 34px;
  font-weight: 700;
  line-height: 1.1;
  color: var(--app-text-primary);
}

.stat-note {
  margin-top: auto;
  font-size: 13px;
  color: var(--app-text-secondary);
}

.recent-card {
  min-width: 0;
}

.card-header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.card-header-tip {
  font-size: var(--app-font-xs);
  color: var(--app-text-secondary);
}

@media (width <= 768px) {
  .page-header {
    flex-direction: column;
  }

  .period-tools {
    justify-content: flex-start;
    width: 100%;
  }

  .period-picker {
    width: 100%;
  }
}
</style>
