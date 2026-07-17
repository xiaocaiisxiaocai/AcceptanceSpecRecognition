<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  getEmbeddingCacheWarmupInfo,
  runEmbeddingCacheWarmup,
  updateEmbeddingCacheWarmupOptions,
  type EmbeddingCacheWarmupInfo,
  type EmbeddingCacheWarmupOptions
} from "@/api/embedding-cache-warmup";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";
import { validateForm } from "@/utils/form-rules";

defineOptions({
  name: "EmbeddingCacheWarmup"
});

const loading = ref(false);
const saving = ref(false);
const running = ref(false);
const info = ref<EmbeddingCacheWarmupInfo | null>(null);

const form = reactive<EmbeddingCacheWarmupOptions>({
  enabled: false,
  runOnStartup: false,
  runAtLocalTime: "",
  intervalHours: 24,
  batchSize: 100,
  maxItemsPerRun: 1000
});
const formRef = ref<FormInstance>();
const positiveNumberRule = (message: string) => ({
  type: "number" as const,
  min: 1,
  message,
  trigger: ["blur", "change"]
});
const formRules: FormRules<EmbeddingCacheWarmupOptions> = {
  intervalHours: [positiveNumberRule("间隔小时数必须大于 0")],
  batchSize: [positiveNumberRule("单批数量必须大于 0")],
  maxItemsPerRun: [
    positiveNumberRule("单轮上限必须大于 0"),
    {
      validator: (_rule, value, callback) => {
        if (form.batchSize > value) {
          callback(new Error("单批数量不能大于单轮上限"));
        } else callback();
      },
      trigger: ["blur", "change"]
    }
  ]
};

const canUpdate = computed(() => hasPerms("btn:embedding-cache-warmup:update"));
const canExecute = computed(() =>
  hasPerms("btn:embedding-cache-warmup:execute")
);

const statusType = computed(() => {
  if (info.value?.status.isRunning) return "warning";
  if (info.value?.status.lastSucceeded === true) return "success";
  if (info.value?.status.lastSucceeded === false) return "danger";
  return "info";
});

const statusText = computed(() => {
  if (info.value?.status.isRunning) return "预热运行中";
  if (info.value?.status.lastSucceeded === true) return "最近执行成功";
  if (info.value?.status.lastSucceeded === false) return "最近执行失败";
  return "尚未执行";
});

const scheduleText = computed(() => {
  if (!form.enabled) return "未启用";
  const runAt = form.runAtLocalTime?.trim();
  if (runAt) return `每天 ${runAt}`;
  return `每 ${form.intervalHours} 小时`;
});

const lastRunText = computed(() => {
  const status = info.value?.status;
  if (!status?.lastStartedAt) return "无执行记录";
  const started = formatDateTime(status.lastStartedAt);
  const finished = status.lastFinishedAt
    ? formatDateTime(status.lastFinishedAt)
    : "运行中";
  return `${started} 至 ${finished}`;
});

const assignOptions = (options: EmbeddingCacheWarmupOptions) => {
  form.enabled = options.enabled;
  form.runOnStartup = options.runOnStartup;
  form.runAtLocalTime = options.runAtLocalTime ?? "";
  form.intervalHours = options.intervalHours;
  form.batchSize = options.batchSize;
  form.maxItemsPerRun = options.maxItemsPerRun;
};

const load = async () => {
  loading.value = true;
  try {
    const res = await getEmbeddingCacheWarmupInfo();
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
    } else {
      ElMessage.error(res.message || "加载预热配置失败");
    }
  } catch {
    ElMessage.error("加载预热配置失败");
  } finally {
    loading.value = false;
  }
};

const buildPayload = (): EmbeddingCacheWarmupOptions => ({
  enabled: form.enabled,
  runOnStartup: form.runOnStartup,
  runAtLocalTime: form.runAtLocalTime?.trim() || "",
  intervalHours: Number(form.intervalHours) || 24,
  batchSize: Number(form.batchSize) || 100,
  maxItemsPerRun: Number(form.maxItemsPerRun) || 1000
});

const save = async () => {
  if (
    !ensurePermission(
      "btn:embedding-cache-warmup:update",
      "权限不足，无法保存预热配置"
    )
  ) {
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  saving.value = true;
  try {
    const res = await updateEmbeddingCacheWarmupOptions(buildPayload());
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
      ElMessage.success("预热配置已更新");
    } else {
      ElMessage.error(res.message || "保存预热配置失败");
    }
  } catch {
    ElMessage.error("保存预热配置失败");
  } finally {
    saving.value = false;
  }
};

const runNow = async () => {
  if (
    !ensurePermission(
      "btn:embedding-cache-warmup:execute",
      "权限不足，无法手动触发预热"
    )
  ) {
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  try {
    await ElMessageBox.confirm(
      "立即预热会调用 Embedding 服务批量生成向量，确认执行吗？",
      "立即预热",
      {
        confirmButtonText: "开始预热",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
  } catch {
    return;
  }

  running.value = true;
  try {
    const res = await runEmbeddingCacheWarmup();
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
      ElMessage.success("已触发预热");
    } else {
      ElMessage.error(res.message || "触发预热失败");
    }
  } catch {
    ElMessage.error("触发预热失败");
  } finally {
    running.value = false;
  }
};

const formatDateTime = (value?: string | null) => {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString();
};

onMounted(load);
</script>

<template>
  <div class="page config-page embedding-warmup-page">
    <div class="page-header">
      <div>
        <div class="page-title">Embedding 缓存预热</div>
      </div>
      <div class="header-actions">
        <el-button :loading="loading" @click="load">刷新</el-button>
        <el-button
          v-if="canExecute"
          type="primary"
          :loading="running || info?.status.isRunning"
          :disabled="info?.status.isRunning"
          @click="runNow"
        >
          立即预热
        </el-button>
      </div>
    </div>

    <el-row :gutter="16" class="summary-row">
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card class="metric-card" shadow="never">
          <div class="metric-label">自动预热</div>
          <div class="metric-value">
            <el-tag :type="form.enabled ? 'success' : 'info'">
              {{ form.enabled ? "已启用" : "未启用" }}
            </el-tag>
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card class="metric-card" shadow="never">
          <div class="metric-label">计划</div>
          <div class="metric-value text-value">{{ scheduleText }}</div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card class="metric-card" shadow="never">
          <div class="metric-label">单轮上限</div>
          <div class="metric-value text-value">
            {{ form.maxItemsPerRun }} 条
          </div>
        </el-card>
      </el-col>
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card class="metric-card" shadow="never">
          <div class="metric-label">运行状态</div>
          <div class="metric-value">
            <el-tag :type="statusType">{{ statusText }}</el-tag>
          </div>
        </el-card>
      </el-col>
    </el-row>

    <el-row :gutter="16">
      <el-col :xs="24" :lg="14">
        <el-card v-loading="loading" shadow="never">
          <template #header>
            <div class="card-header">
              <span>运行期配置</span>
              <el-button
                v-if="canUpdate"
                type="primary"
                :loading="saving"
                @click="save"
              >
                保存配置
              </el-button>
            </div>
          </template>

          <el-form
            ref="formRef"
            :model="form"
            :rules="formRules"
            label-width="130px"
            class="warmup-form"
            status-icon
          >
            <el-form-item label="启用自动预热">
              <el-switch v-model="form.enabled" />
            </el-form-item>
            <el-form-item label="启动后执行一次">
              <el-switch v-model="form.runOnStartup" />
            </el-form-item>
            <el-form-item label="每日执行时间">
              <el-time-picker
                v-model="form.runAtLocalTime"
                value-format="HH:mm"
                format="HH:mm"
                placeholder="不指定则按间隔执行"
                clearable
              />
            </el-form-item>
            <el-form-item label="间隔小时数" prop="intervalHours">
              <el-input-number
                v-model="form.intervalHours"
                :min="1"
                :max="168"
                controls-position="right"
              />
            </el-form-item>
            <el-form-item label="单批数量" prop="batchSize">
              <el-input-number
                v-model="form.batchSize"
                :min="1"
                :max="1000"
                controls-position="right"
              />
            </el-form-item>
            <el-form-item label="单轮最多处理" prop="maxItemsPerRun">
              <el-input-number
                v-model="form.maxItemsPerRun"
                :min="1"
                :max="100000"
                controls-position="right"
              />
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <el-col :xs="24" :lg="10">
        <el-card v-loading="loading" shadow="never">
          <template #header>
            <span>最近执行</span>
          </template>

          <el-descriptions :column="1" border>
            <el-descriptions-item label="状态">
              <el-tag :type="statusType">{{ statusText }}</el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="时间">
              {{ lastRunText }}
            </el-descriptions-item>
            <el-descriptions-item label="批大小">
              {{ info?.status.lastBatchSize ?? "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="单轮上限">
              {{ info?.status.lastMaxItemsPerRun ?? "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="错误">
              <span class="error-text">{{
                info?.status.lastError || "-"
              }}</span>
            </el-descriptions-item>
          </el-descriptions>

          <el-alert
            class="mt-4"
            type="info"
            :closable="false"
            show-icon
            title="页面保存的是当前 API 进程的运行期配置；部署配置仍以服务器配置文件为准。"
          />
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<style scoped>
.embedding-warmup-page {
  padding: 0;
  overflow-x: hidden;
}

.embedding-warmup-page :deep(.el-row) {
  margin-right: 0 !important;
  margin-left: 0 !important;
}

.page-header {
  margin-bottom: 16px;
}

.page-title {
  margin: 0;
}

.header-actions,
.card-header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.summary-row {
  margin-bottom: 16px;
}

.metric-card {
  height: 100%;
  margin-bottom: 16px;
}

.metric-label {
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.metric-value {
  display: flex;
  align-items: center;
  min-height: 28px;
  margin-top: 10px;
}

.text-value {
  font-size: 20px;
  font-weight: 650;
}

.warmup-form {
  max-width: 560px;
}

.error-text {
  color: var(--el-color-danger);
  word-break: break-word;
}

.mt-4 {
  margin-top: 16px;
}

@media (width <= 768px) {
  .page-header,
  .card-header {
    flex-direction: column;
    align-items: stretch;
  }

  .header-actions {
    justify-content: flex-start;
  }
}
</style>
