<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  getDatabaseBackupInfo,
  runDatabaseBackup,
  updateDatabaseBackupOptions,
  type DatabaseBackupInfo,
  type DatabaseBackupOptions
} from "@/api/database-backup";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";
import { requiredTrimmedRule, validateForm } from "@/utils/form-rules";

defineOptions({
  name: "DatabaseBackup"
});

const loading = ref(false);
const saving = ref(false);
const running = ref(false);
const info = ref<DatabaseBackupInfo | null>(null);

const form = reactive<DatabaseBackupOptions>({
  enabled: false,
  runAtLocalTime: "02:00",
  backupDirectory: "/app/backups",
  retentionCount: 7
});
const formRef = ref<FormInstance>();
const formRules: FormRules<DatabaseBackupOptions> = {
  backupDirectory: [requiredTrimmedRule("备份目录不能为空")],
  retentionCount: [
    {
      type: "number",
      min: 1,
      message: "保留份数必须大于 0",
      trigger: ["blur", "change"]
    }
  ]
};

const canUpdate = computed(() => hasPerms("btn:database-backup:update"));
const canExecute = computed(() => hasPerms("btn:database-backup:execute"));

const statusType = computed(() => {
  if (info.value?.status.isRunning) return "warning";
  if (info.value?.status.lastSucceeded === true) return "success";
  if (info.value?.status.lastSucceeded === false) return "danger";
  return "info";
});

const statusText = computed(() => {
  if (info.value?.status.isRunning) return "备份运行中";
  if (info.value?.status.lastSucceeded === true) return "最近备份成功";
  if (info.value?.status.lastSucceeded === false) return "最近备份失败";
  return "尚未备份";
});

const scheduleText = computed(() => {
  if (!form.enabled) return "未启用";
  return `每天 ${form.runAtLocalTime || "02:00"}`;
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

const assignOptions = (options: DatabaseBackupOptions) => {
  form.enabled = options.enabled;
  form.runAtLocalTime = options.runAtLocalTime || "02:00";
  form.backupDirectory = options.backupDirectory || "/app/backups";
  form.retentionCount = options.retentionCount || 7;
};

const load = async () => {
  loading.value = true;
  try {
    const res = await getDatabaseBackupInfo();
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
    } else {
      ElMessage.error(res.message || "加载数据库备份配置失败");
    }
  } catch {
    ElMessage.error("加载数据库备份配置失败");
  } finally {
    loading.value = false;
  }
};

const buildPayload = (): DatabaseBackupOptions => ({
  enabled: form.enabled,
  runAtLocalTime: form.runAtLocalTime?.trim() || "02:00",
  backupDirectory: form.backupDirectory?.trim() || "/app/backups",
  retentionCount: Number(form.retentionCount) || 7
});

const save = async () => {
  if (
    !ensurePermission(
      "btn:database-backup:update",
      "权限不足，无法保存数据库备份配置"
    )
  ) {
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  saving.value = true;
  try {
    const res = await updateDatabaseBackupOptions(buildPayload());
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
      ElMessage.success("数据库备份配置已更新");
    } else {
      ElMessage.error(res.message || "保存数据库备份配置失败");
    }
  } catch {
    ElMessage.error("保存数据库备份配置失败");
  } finally {
    saving.value = false;
  }
};

const runNow = async () => {
  if (
    !ensurePermission(
      "btn:database-backup:execute",
      "权限不足，无法手动执行数据库备份"
    )
  ) {
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  try {
    await ElMessageBox.confirm(
      "立即备份会调用 mysqldump 导出当前数据库，确认执行吗？",
      "立即备份",
      {
        confirmButtonText: "开始备份",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
  } catch {
    return;
  }

  running.value = true;
  try {
    const res = await runDatabaseBackup();
    if (res.code === 0) {
      info.value = res.data;
      assignOptions(res.data.options);
      ElMessage.success("数据库备份完成");
    } else {
      ElMessage.error(res.message || "数据库备份失败");
    }
  } catch {
    ElMessage.error("数据库备份失败");
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

const formatFileSize = (value?: number | null) => {
  if (!value) return "-";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  if (value < 1024 * 1024 * 1024)
    return `${(value / 1024 / 1024).toFixed(1)} MB`;
  return `${(value / 1024 / 1024 / 1024).toFixed(1)} GB`;
};

onMounted(load);
</script>

<template>
  <div class="page page--fill config-page database-backup-page">
    <div class="page-header">
      <div class="header-actions">
        <el-button :loading="loading" @click="load">刷新</el-button>
        <el-button
          v-if="canExecute"
          type="primary"
          :loading="running || info?.status.isRunning"
          :disabled="info?.status.isRunning"
          @click="runNow"
        >
          立即备份
        </el-button>
      </div>
    </div>

    <el-row :gutter="16" class="summary-row">
      <el-col :xs="24" :sm="12" :lg="6">
        <el-card class="metric-card" shadow="never">
          <div class="metric-label">自动备份</div>
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
          <div class="metric-label">保留份数</div>
          <div class="metric-value text-value">
            {{ form.retentionCount }} 份
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

    <el-row :gutter="16" class="config-row">
      <el-col :xs="24" :lg="14">
        <el-card v-loading="loading" shadow="never">
          <template #header>
            <div class="card-header">
              <span>备份配置</span>
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
            label-width="120px"
            class="backup-form"
            status-icon
          >
            <el-form-item label="启用自动备份">
              <el-switch v-model="form.enabled" />
            </el-form-item>
            <el-form-item label="每日备份时间">
              <el-time-picker
                v-model="form.runAtLocalTime"
                value-format="HH:mm"
                format="HH:mm"
                placeholder="选择备份时间"
                clearable
              />
            </el-form-item>
            <el-form-item label="备份目录" prop="backupDirectory">
              <el-input
                v-model="form.backupDirectory"
                placeholder="/app/backups"
                clearable
              />
            </el-form-item>
            <el-form-item label="保留份数" prop="retentionCount">
              <el-input-number
                v-model="form.retentionCount"
                :min="1"
                :max="365"
                controls-position="right"
              />
            </el-form-item>
          </el-form>

          <el-alert
            class="mt-4"
            type="warning"
            :closable="false"
            show-icon
            title="备份目录需要映射到宿主机或外部卷；否则容器重建后备份文件会丢失。"
          />
        </el-card>
      </el-col>

      <el-col :xs="24" :lg="10">
        <el-card v-loading="loading" shadow="never">
          <template #header>
            <span>最近备份</span>
          </template>

          <el-descriptions :column="1" border>
            <el-descriptions-item label="状态">
              <el-tag :type="statusType">{{ statusText }}</el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="时间">
              {{ lastRunText }}
            </el-descriptions-item>
            <el-descriptions-item label="文件">
              {{ info?.status.lastFileName || "-" }}
            </el-descriptions-item>
            <el-descriptions-item label="大小">
              {{ formatFileSize(info?.status.lastFileSizeBytes) }}
            </el-descriptions-item>
            <el-descriptions-item label="错误">
              <span class="error-text">{{
                info?.status.lastError || "-"
              }}</span>
            </el-descriptions-item>
          </el-descriptions>
        </el-card>
      </el-col>
    </el-row>

    <el-card v-loading="loading" class="file-card" shadow="never">
      <template #header>
        <span>备份文件</span>
      </template>

      <el-table :data="info?.files ?? []" height="100%" stripe>
        <el-table-column
          prop="fileName"
          label="文件名"
          min-width="min(260px, calc(100vw - 32px))"
        />
        <el-table-column label="大小" width="min(120px, calc(100vw - 32px))">
          <template #default="{ row }">
            {{ formatFileSize(row.sizeBytes) }}
          </template>
        </el-table-column>
        <el-table-column
          label="创建时间"
          width="min(220px, calc(100vw - 32px))"
        >
          <template #default="{ row }">
            {{ formatDateTime(row.createdAt) }}
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无备份文件" />
        </template>
      </el-table>
    </el-card>
  </div>
</template>

<style scoped>
.database-backup-page.page--fill {
  padding: 0;
  overflow: hidden auto !important;
}

.database-backup-page :deep(.el-row) {
  margin-right: 0 !important;
  margin-left: 0 !important;
}

.page-header {
  justify-content: flex-end;
}

.header-actions,
.card-header {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.summary-row,
.config-row {
  flex: 0 0 auto;
  row-gap: 12px;
}

.metric-card {
  height: 100%;
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

.backup-form {
  max-width: 620px;
}

.error-text {
  color: var(--el-color-danger);
  word-break: break-word;
}

.mt-4 {
  margin-top: 16px;
}

.file-card {
  display: flex;
  flex: 1 1 260px;
  flex-direction: column;
  min-height: 260px;
}

.file-card :deep(.el-card__body) {
  display: flex;
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

.file-card :deep(.el-table) {
  flex: 1;
  min-height: 0;
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
