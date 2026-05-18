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
const periodPreset = ref<DashboardPeriodPreset>("last7");
const customRange = ref<[Date, Date] | null>(null);
const summary = ref<DashboardSummary | null>(null);
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

const scheduleLoad = () => {
  if (typeof idleScheduler.requestIdleCallback === "function") {
    idleCallbackId = idleScheduler.requestIdleCallback(
      () => {
        void load();
      },
      { timeout: 800 }
    );
    return;
  }

  loadTimerId = globalThis.setTimeout(() => {
    void load();
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

const formatPercent = (value: number | undefined) => {
  return `${Math.round((value ?? 0) * 100)}%`;
};

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

onMounted(scheduleLoad);

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
        <div class="page-subtitle">
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
        <el-button :icon="Refresh" :loading="loading" @click="load">
          刷新
        </el-button>
      </div>
    </div>

    <el-row :gutter="16">
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card stat-card--primary">
          <div class="stat">
            <div class="stat-title">匹配度</div>
            <div class="stat-value">
              {{ formatPercent(currentSummary.matchingRate) }}
            </div>
            <div class="stat-note">
              已匹配 {{ formatNumber(currentSummary.smartFillMatchedRows) }} /
              {{ formatNumber(currentSummary.smartFillTotalRows) }} 行
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card stat-card--import">
          <div class="stat">
            <div class="stat-title">历史资料导入</div>
            <div class="stat-value">
              {{ formatNumber(currentSummary.importedSpecCount) }}
            </div>
            <div class="stat-note">周期内新增或更新规格</div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-title">智能填充任务</div>
            <div class="stat-value">
              {{ formatNumber(currentSummary.smartFillTaskCount) }}
            </div>
            <div class="stat-note">
              采用率 {{ formatPercent(currentSummary.adoptionRate) }}
            </div>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card v-loading="loading" class="stat-card">
          <div class="stat">
            <div class="stat-title">验收规格总量</div>
            <div class="stat-value">
              {{ formatNumber(currentSummary.specTotal) }}
            </div>
            <div class="stat-note">
              客户 {{ formatNumber(currentSummary.customerTotal) }}，制程
              {{ formatNumber(currentSummary.processTotal) }}
            </div>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.page-title {
  font-size: 22px;
  font-weight: 650;
  color: var(--color-text);
}

.page-subtitle {
  margin-top: 6px;
  font-size: 13px;
  color: #6b7280;
}

.period-tools {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 10px;
}

.period-picker {
  width: 360px;
}

.stat-card {
  border: 1px solid #e5e7eb;
  background: #ffffff;
}

.stat-card--primary {
  border-color: #b9d7c5;
  background: linear-gradient(180deg, #ffffff 0%, #f3faf5 100%);
}

.stat-card--import {
  border-color: #c7d2fe;
  background: linear-gradient(180deg, #ffffff 0%, #f6f8ff 100%);
}

.stat {
  min-height: 118px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.stat-title {
  color: #5f6876;
  font-size: 14px;
}

.stat-value {
  line-height: 1.1;
  font-size: 34px;
  font-weight: 700;
  color: #1f2937;
}

.stat-note {
  margin-top: auto;
  color: #6b7280;
  font-size: 13px;
}

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
  }

  .period-tools {
    width: 100%;
    justify-content: flex-start;
  }

  .period-picker {
    width: 100%;
  }
}
</style>
