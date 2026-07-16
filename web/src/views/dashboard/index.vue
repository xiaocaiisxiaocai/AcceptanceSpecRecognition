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
  adoptionRate: 0
};

const loading = ref(false);
const recentLoading = ref(false);
const periodPreset = ref<DashboardPeriodPreset>("last7");
const customRange = ref<[Date, Date] | null>(null);
const summary = ref<DashboardSummary | null>(null);
const recentTasks = ref<ExecutionHistoryListItem[]>([]);
const trendLoading = ref(false);
const trendLabels = ref<string[]>([]);
const importTrend = ref<number[]>([]);
const taskTrend = ref<number[]>([]);
const datePickerDefaultTime: [Date, Date] = [
  new Date(2000, 0, 1, 0, 0, 0),
  new Date(2000, 0, 1, 23, 59, 59)
];

const currentSummary = computed(() => summary.value ?? emptySummary);
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
  loading.value = true;
  try {
    const response = await getDashboardSummary(buildRequest());
    if (response.code === 0) {
      summary.value = response.data;
      periodPreset.value = response.data.periodPreset;
    }
  } catch {
    ElMessage.error("加载首页统计数据失败");
  } finally {
    loading.value = false;
  }
};

const buildDailyTrendRequests = () => {
  const today = new Date();

  return Array.from({ length: 7 }, (_, index) => {
    const day = new Date(today);
    day.setDate(today.getDate() - (6 - index));
    const from = new Date(day);
    const to = new Date(day);
    from.setHours(0, 0, 0, 0);
    to.setHours(23, 59, 59, 999);

    return {
      label: `${day.getMonth() + 1}/${day.getDate()}`,
      request: getDashboardSummary({
        range: "custom",
        from: from.toISOString(),
        to: to.toISOString()
      })
    };
  });
};

const loadTrends = async () => {
  trendLoading.value = true;
  const dailyRequests = buildDailyTrendRequests();

  try {
    const responses = await Promise.all(
      dailyRequests.map(item => item.request)
    );
    trendLabels.value = dailyRequests.map(item => item.label);
    importTrend.value = responses.map(item =>
      item.code === 0 ? item.data.importedSpecCount : 0
    );
    taskTrend.value = responses.map(item =>
      item.code === 0 ? item.data.smartFillTaskCount : 0
    );
  } catch {
    trendLabels.value = dailyRequests.map(item => item.label);
    importTrend.value = Array(7).fill(0);
    taskTrend.value = Array(7).fill(0);
  } finally {
    trendLoading.value = false;
  }
};

const loadRecentTasks = async () => {
  recentLoading.value = true;
  try {
    const response = await getExecutionHistoryList({ page: 1, pageSize: 5 });
    if (response.code === 0) {
      recentTasks.value = response.data.items;
      return;
    }

    ElMessage.error(response.message || "加载最近执行记录失败");
  } catch {
    ElMessage.error("加载最近执行记录失败");
  } finally {
    recentLoading.value = false;
  }
};

const scheduleLoad = () => {
  if (typeof idleScheduler.requestIdleCallback === "function") {
    idleCallbackId = idleScheduler.requestIdleCallback(
      () => {
        void load();
        void loadRecentTasks();
        void loadTrends();
      },
      { timeout: 800 }
    );
    return;
  }

  loadTimerId = globalThis.setTimeout(() => {
    void load();
    void loadRecentTasks();
    void loadTrends();
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
  void loadTrends();
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
});
</script>

<template>
  <div class="page dashboard">
    <div class="page-header">
      <div>
        <div class="page-title">系统概览</div>
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
        <el-button
          :icon="Refresh"
          :loading="loading || trendLoading"
          @click="refreshDashboard"
        >
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
              <span class="stat-period">近 7 天</span>
            </div>
            <DashboardSparkline
              :values="importTrend"
              :labels="trendLabels"
              :loading="trendLoading"
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
              <span class="stat-period">近 7 天</span>
            </div>
            <DashboardSparkline
              :values="taskTrend"
              :labels="trendLabels"
              :loading="trendLoading"
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
          min-width="min(220px, calc(100vw - 32px))"
          show-overflow-tooltip
        />
        <el-table-column
          label="采用/总行"
          width="min(110px, calc(100vw - 32px))"
        >
          <template #default="{ row }">
            {{ formatNumber(row.adoptedRowCount) }} /
            {{ formatNumber(row.totalRowCount) }}
          </template>
        </el-table-column>
        <el-table-column label="时间" width="min(160px, calc(100vw - 32px))">
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

.page-header {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
}

.page-title {
  font-size: var(--app-font-xl);
  font-weight: 650;
  color: var(--app-text-primary);
}

.dashboard-period-range {
  margin-top: 4px;
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
