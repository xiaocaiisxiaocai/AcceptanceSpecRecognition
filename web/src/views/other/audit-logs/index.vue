<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { useMediaQuery } from "@vueuse/core";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  AuditLogLevel,
  AuditLogSource,
  deleteAuditLogsByRange,
  getAuditLogDetail,
  getAuditLogList,
  type AuditLogDetail,
  type AuditLogListItem
} from "@/api/audit-log";
import { hasPerms } from "@/utils/auth";
import { parseApiUtcDateTime, toApiUtcDateTime } from "@/utils/date-time";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import { ensurePermission } from "@/utils/permission-guard";
import { requiredSelectionRule, validateForm } from "@/utils/form-rules";

defineOptions({
  name: "AuditLogs"
});

const loading = ref(false);
const deleting = ref(false);
const advancedFiltersVisible = ref(false);
const deleteDialogVisible = ref(false);
const tableData = ref<AuditLogListItem[]>([]);
const total = ref(0);
const queryRange = ref<Date[]>([]);
const deleteForm = reactive({ range: [] as Date[] });
const deleteFormRef = ref<FormInstance>();
const deleteFormRules: FormRules<typeof deleteForm> = {
  range: [requiredSelectionRule("请选择删除时间范围")]
};

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  source: AuditLogSource.BackendRequest as AuditLogSource | undefined,
  level: undefined as AuditLogLevel | undefined,
  username: "",
  requestMethod: "",
  keyword: ""
});

const sourceOptions = [
  { label: "控制器操作", value: AuditLogSource.BackendRequest },
  { label: "前端事件", value: AuditLogSource.FrontendEvent }
];

const levelOptions = [
  { label: "信息", value: AuditLogLevel.Information },
  { label: "警告", value: AuditLogLevel.Warning },
  { label: "错误", value: AuditLogLevel.Error }
];

const methodOptions = ["POST", "PUT", "DELETE", "PATCH"];

const detailVisible = ref(false);
const detailLoading = ref(false);
const currentDetail = ref<AuditLogDetail | null>(null);
const isNarrowScreen = useMediaQuery("(max-width: 768px)");
const canDeleteRange = computed(() => hasPerms("btn:audit-log:delete-range"));
const detailDescriptionColumns = computed(() => (isNarrowScreen.value ? 1 : 2));

const eventLabels: Record<string, string> = {
  "controller.refresh-token": "刷新令牌",
  "controller.login": "用户登录",
  "controller.logout": "用户退出",
  "audit-log.delete-range": "清理审计日志",
  "frontend.error": "前端异常",
  "frontend.navigation": "页面访问"
};

const formattedDetails = computed(() => {
  if (!currentDetail.value?.details) return "-";
  try {
    return JSON.stringify(JSON.parse(currentDetail.value.details), null, 2);
  } catch {
    return currentDetail.value.details;
  }
});

const getLevelLabel = (level: AuditLogLevel) => {
  return levelOptions.find(x => x.value === level)?.label ?? String(level);
};

const getLevelType = (level: AuditLogLevel) => {
  if (level === AuditLogLevel.Error) return "danger";
  if (level === AuditLogLevel.Warning) return "warning";
  return "info";
};

const getSourceLabel = (source?: AuditLogSource) => {
  if (source === undefined) return "-";
  return sourceOptions.find(x => x.value === source)?.label ?? String(source);
};

const formatEventLabel = (eventType?: string | null) => {
  if (!eventType) return "-";
  return eventLabels[eventType] ?? eventType;
};

const formatUser = (username?: string | null) => {
  return username?.trim() || "系统";
};

const formatDateTime = (value?: string | null) => {
  if (!value) return "-";
  const date = parseApiUtcDateTime(value);
  if (!date) return value;
  const pad = (part: number) => String(part).padStart(2, "0");
  return (
    [date.getFullYear(), pad(date.getMonth() + 1), pad(date.getDate())].join(
      "-"
    ) +
    ` ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`
  );
};

const getStatusType = (statusCode?: number | null) => {
  if (statusCode == null) return "info";
  if (statusCode >= 500) return "danger";
  if (statusCode >= 400) return "warning";
  if (statusCode >= 200 && statusCode < 400) return "success";
  return "info";
};

const getDurationClass = (durationMs?: number | null) => ({
  "is-warning": durationMs != null && durationMs >= 500 && durationMs < 1000,
  "is-slow": durationMs != null && durationMs >= 1000
});

const copyText = async (text: string) => {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(text);
    return;
  }

  const textarea = document.createElement("textarea");
  textarea.value = text;
  textarea.style.position = "fixed";
  textarea.style.opacity = "0";
  document.body.appendChild(textarea);
  textarea.select();
  document.execCommand("copy");
  textarea.remove();
};

const copyDetails = async () => {
  try {
    await copyText(formattedDetails.value);
    ElMessage.success("详情已复制");
  } catch {
    ElMessage.error("复制失败，请手动复制");
  }
};

const loadData = async () => {
  loading.value = true;
  try {
    const [from, to] = queryRange.value ?? [];
    const res = await getAuditLogList({
      page: queryParams.page,
      pageSize: queryParams.pageSize,
      source: queryParams.source,
      level: queryParams.level,
      username: queryParams.username || undefined,
      requestMethod: queryParams.requestMethod || undefined,
      keyword: queryParams.keyword || undefined,
      from: toApiUtcDateTime(from),
      to: toApiUtcDateTime(to)
    });

    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载审计日志失败");
  } finally {
    loading.value = false;
  }
};

const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};

const handleReset = () => {
  queryParams.page = 1;
  queryParams.pageSize = 20;
  queryParams.source = AuditLogSource.BackendRequest;
  queryParams.level = undefined;
  queryParams.username = "";
  queryParams.requestMethod = "";
  queryParams.keyword = "";
  queryRange.value = [];
  loadData();
};

const openDeleteDialog = () => {
  if (
    !ensurePermission(
      "btn:audit-log:delete-range",
      "权限不足，无法删除审计日志"
    )
  ) {
    return;
  }
  deleteDialogVisible.value = true;
};

const handleDeleteByRange = async () => {
  if (
    !ensurePermission(
      "btn:audit-log:delete-range",
      "权限不足，无法删除审计日志"
    )
  ) {
    return;
  }
  if (!(await validateForm(deleteFormRef.value))) return;
  const [from, to] = deleteForm.range ?? [];

  try {
    await ElMessageBox.confirm(
      "删除后不可恢复，确认删除该时间范围内的审计日志吗？",
      "确认删除",
      {
        type: "warning",
        confirmButtonText: "确认删除",
        cancelButtonText: "取消"
      }
    );

    deleting.value = true;
    const res = await deleteAuditLogsByRange({
      from: toApiUtcDateTime(from),
      to: toApiUtcDateTime(to)
    });
    if (res.code === 0) {
      ElMessage.success(res.message || "删除成功");
      deleteForm.range = [];
      deleteDialogVisible.value = false;
      queryParams.page = 1;
      await loadData();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除失败"));
  } finally {
    deleting.value = false;
  }
};

const openDetail = async (row: AuditLogListItem) => {
  detailVisible.value = true;
  currentDetail.value = null;
  detailLoading.value = true;
  try {
    const res = await getAuditLogDetail(row.id);
    if (res.code === 0) {
      currentDetail.value = res.data;
    } else {
      ElMessage.error(res.message || "加载详情失败");
    }
  } catch {
    ElMessage.error("加载详情失败");
  } finally {
    detailLoading.value = false;
  }
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadData();
};

onMounted(loadData);
</script>

<template>
  <div class="page page--fill audit-logs-page">
    <el-card class="table-card audit-table-card" shadow="never">
      <template #header>
        <div class="audit-toolbar">
          <div class="audit-toolbar__main">
            <div class="audit-toolbar__summary">
              <strong>{{ total }}</strong>
              <span>条审计记录</span>
            </div>
            <el-form :inline="true" class="filter-form audit-primary-filter">
              <el-form-item label="级别">
                <el-select
                  v-model="queryParams.level"
                  clearable
                  placeholder="全部"
                  class="search-select search-select--160"
                  popper-class="app-select-popper"
                >
                  <el-option
                    v-for="opt in levelOptions"
                    :key="opt.value"
                    :label="opt.label"
                    :value="opt.value"
                  />
                </el-select>
              </el-form-item>

              <el-form-item label="查询时间">
                <el-date-picker
                  v-model="queryRange"
                  class="audit-date-filter"
                  type="datetimerange"
                  unlink-panels
                  format="YYYY-MM-DD HH:mm:ss"
                  start-placeholder="开始时间"
                  end-placeholder="结束时间"
                />
              </el-form-item>

              <el-form-item label="关键词">
                <el-input
                  v-model="queryParams.keyword"
                  class="audit-keyword-filter"
                  clearable
                  placeholder="路径 / 事件 / 详情"
                  @keyup.enter="handleSearch"
                />
              </el-form-item>

              <el-form-item>
                <el-button type="primary" @click="handleSearch">搜索</el-button>
                <el-button @click="handleReset">重置</el-button>
                <el-button
                  link
                  type="primary"
                  @click="advancedFiltersVisible = !advancedFiltersVisible"
                >
                  {{ advancedFiltersVisible ? "收起筛选" : "高级筛选" }}
                </el-button>
              </el-form-item>
            </el-form>
          </div>
          <el-button
            v-if="canDeleteRange"
            type="danger"
            plain
            @click="openDeleteDialog"
          >
            日志清理
          </el-button>
        </div>

        <el-collapse-transition>
          <el-form
            v-show="advancedFiltersVisible"
            :inline="true"
            class="filter-form audit-advanced-filter"
          >
            <el-form-item label="来源">
              <el-select
                v-model="queryParams.source"
                clearable
                placeholder="全部"
                class="search-select search-select--160"
                popper-class="app-select-popper"
              >
                <el-option
                  v-for="opt in sourceOptions"
                  :key="opt.value"
                  :label="opt.label"
                  :value="opt.value"
                />
              </el-select>
            </el-form-item>

            <el-form-item label="用户">
              <el-input
                v-model="queryParams.username"
                class="audit-user-filter"
                clearable
                placeholder="用户名"
                @keyup.enter="handleSearch"
              />
            </el-form-item>

            <el-form-item label="方法">
              <el-select
                v-model="queryParams.requestMethod"
                clearable
                placeholder="全部"
                class="search-select search-select--160"
                popper-class="app-select-popper"
              >
                <el-option
                  v-for="method in methodOptions"
                  :key="method"
                  :label="method"
                  :value="method"
                />
              </el-select>
            </el-form-item>
          </el-form>
        </el-collapse-transition>
      </template>

      <div class="table-region">
        <el-table v-loading="loading" :data="tableData" stripe height="100%">
          <el-table-column prop="id" label="ID" width="80" />
          <el-table-column label="级别" width="100">
            <template #default="{ row }">
              <el-tag
                :type="getLevelType(row.level)"
                effect="plain"
                size="small"
              >
                {{ getLevelLabel(row.level) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="事件" width="160">
            <template #default="{ row }">
              <el-tooltip
                :content="row.eventType"
                placement="top"
                :show-after="400"
              >
                <span class="audit-event-label">
                  {{ formatEventLabel(row.eventType) }}
                </span>
              </el-tooltip>
            </template>
          </el-table-column>
          <el-table-column label="用户" width="110" show-overflow-tooltip>
            <template #default="{ row }">
              <span :class="{ 'system-user': !row.username }">
                {{ formatUser(row.username) }}
              </span>
            </template>
          </el-table-column>
          <el-table-column label="方法" width="82">
            <template #default="{ row }">
              <span v-if="row.requestMethod" class="request-method">
                {{ row.requestMethod }}
              </span>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column
            prop="requestPath"
            label="请求路径"
            min-width="220"
            show-overflow-tooltip
          >
            <template #default="{ row }">
              <span class="request-path">{{ row.requestPath || "-" }}</span>
            </template>
          </el-table-column>
          <el-table-column
            prop="frontendRoute"
            label="前端路由"
            min-width="180"
            show-overflow-tooltip
          />
          <el-table-column label="状态" width="82">
            <template #default="{ row }">
              <el-tag
                v-if="row.statusCode != null"
                :type="getStatusType(row.statusCode)"
                effect="light"
                size="small"
              >
                {{ row.statusCode }}
              </el-tag>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column label="耗时" width="96">
            <template #default="{ row }">
              <span
                class="duration-value"
                :class="getDurationClass(row.durationMs)"
              >
                {{ row.durationMs == null ? "-" : `${row.durationMs} ms` }}
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="createdAt" label="时间" width="170">
            <template #default="{ row }">
              {{ formatDateTime(row.createdAt) }}
            </template>
          </el-table-column>
          <el-table-column label="详情" width="90" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" link @click="openDetail(row)">
                详情
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="queryParams.page"
          v-model:page-size="queryParams.pageSize"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="handleSizeChange"
          @current-change="handlePageChange"
        />
      </div>
    </el-card>

    <el-dialog
      v-model="deleteDialogVisible"
      title="日志清理"
      width="520px"
      append-to-body
      destroy-on-close
    >
      <el-alert
        title="删除操作不可恢复，请先确认时间范围。"
        type="warning"
        :closable="false"
        show-icon
      />
      <el-form
        ref="deleteFormRef"
        :model="deleteForm"
        :rules="deleteFormRules"
        label-position="top"
        class="delete-form"
      >
        <el-form-item label="删除时间范围" prop="range">
          <el-date-picker
            v-model="deleteForm.range"
            type="datetimerange"
            unlink-panels
            format="YYYY-MM-DD HH:mm:ss"
            start-placeholder="开始时间"
            end-placeholder="结束时间"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="deleteDialogVisible = false">取消</el-button>
        <el-button
          type="danger"
          :loading="deleting"
          @click="handleDeleteByRange"
        >
          确认清理
        </el-button>
      </template>
    </el-dialog>

    <el-drawer
      v-model="detailVisible"
      class="audit-detail-drawer"
      size="min(760px, 92vw)"
      append-to-body
      destroy-on-close
    >
      <template #header>
        <div class="drawer-heading">
          <span class="drawer-heading__eyebrow">审计详情</span>
          <strong>{{ formatEventLabel(currentDetail?.eventType) }}</strong>
          <span v-if="currentDetail" class="drawer-heading__meta">
            记录 #{{ currentDetail.id }}
          </span>
        </div>
      </template>

      <div v-loading="detailLoading">
        <div v-if="currentDetail" class="detail-summary">
          <div class="detail-summary__item">
            <span>响应状态</span>
            <el-tag
              v-if="currentDetail.statusCode != null"
              :type="getStatusType(currentDetail.statusCode)"
              effect="light"
            >
              {{ currentDetail.statusCode }}
            </el-tag>
            <strong v-else>-</strong>
          </div>
          <div class="detail-summary__item">
            <span>请求耗时</span>
            <strong :class="getDurationClass(currentDetail.durationMs)">
              {{
                currentDetail.durationMs == null
                  ? "-"
                  : `${currentDetail.durationMs} ms`
              }}
            </strong>
          </div>
          <div class="detail-summary__item">
            <span>发生时间</span>
            <strong>{{ formatDateTime(currentDetail.createdAt) }}</strong>
          </div>
        </div>

        <el-descriptions
          v-if="currentDetail"
          :column="detailDescriptionColumns"
          border
          class="detail-descriptions"
        >
          <el-descriptions-item label="ID">
            {{ currentDetail?.id }}
          </el-descriptions-item>
          <el-descriptions-item label="来源">
            {{ getSourceLabel(currentDetail?.source) }}
          </el-descriptions-item>
          <el-descriptions-item label="事件">
            {{ currentDetail?.eventType || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="用户">
            {{ formatUser(currentDetail?.username) }}
          </el-descriptions-item>
          <el-descriptions-item label="级别">
            {{ getLevelLabel(currentDetail.level) }}
          </el-descriptions-item>
          <el-descriptions-item label="请求方法">
            {{ currentDetail?.requestMethod || "-" }}
          </el-descriptions-item>
          <el-descriptions-item
            label="请求路径"
            :span="detailDescriptionColumns"
          >
            {{ currentDetail?.requestPath || "-" }}
          </el-descriptions-item>
          <el-descriptions-item
            label="查询参数"
            :span="detailDescriptionColumns"
          >
            {{ currentDetail?.queryString || "-" }}
          </el-descriptions-item>
          <el-descriptions-item
            label="前端路由"
            :span="detailDescriptionColumns"
          >
            {{ currentDetail?.frontendRoute || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="客户端 IP">
            {{ currentDetail?.clientIp || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="客户端 ID">
            {{ currentDetail?.clientId || "-" }}
          </el-descriptions-item>
          <el-descriptions-item
            label="跟踪 ID"
            :span="detailDescriptionColumns"
          >
            {{ currentDetail?.clientTraceId || "-" }}
          </el-descriptions-item>
          <el-descriptions-item
            label="User Agent"
            :span="detailDescriptionColumns"
          >
            {{ currentDetail?.userAgent || "-" }}
          </el-descriptions-item>
        </el-descriptions>

        <div class="detail-content">
          <div class="detail-title">
            <span>详情内容</span>
            <el-button
              type="primary"
              link
              :disabled="!currentDetail?.details"
              @click="copyDetails"
            >
              复制 JSON
            </el-button>
          </div>
          <pre tabindex="0">{{ formattedDetails }}</pre>
        </div>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.audit-toolbar {
  display: flex;
  gap: 16px;
  align-items: flex-start;
  justify-content: space-between;
}

.audit-toolbar__main {
  display: flex;
  flex: 1;
  gap: 20px;
  align-items: center;
  min-width: 0;
}

.audit-toolbar__summary {
  display: flex;
  flex: none;
  gap: 6px;
  align-items: baseline;
  min-width: 96px;
  color: var(--el-text-color-secondary);
  white-space: nowrap;
}

.audit-toolbar__summary strong {
  font-size: 22px;
  line-height: 1;
  color: var(--el-color-primary);
}

.audit-primary-filter {
  flex: 1;
  min-width: 0;
  min-height: 0;
}

.audit-advanced-filter {
  padding: 12px 0 2px;
  margin-top: 12px;
  border-top: 1px solid var(--el-border-color-lighter);
}

.audit-user-filter {
  width: 150px;
}

.audit-date-filter {
  width: 320px;
}

.audit-keyword-filter {
  width: 220px;
}

.audit-event-label {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 600;
  color: var(--el-text-color-primary);
  white-space: nowrap;
}

.system-user {
  color: var(--el-text-color-secondary);
}

.request-method,
.request-path {
  font-family: SFMono-Regular, Consolas, "Liberation Mono", monospace;
  font-size: 12px;
}

.request-method {
  padding: 2px 6px;
  color: var(--el-text-color-regular);
  background: var(--el-fill-color-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
}

.duration-value {
  font-variant-numeric: tabular-nums;
  color: var(--el-text-color-regular);
}

.is-warning {
  color: var(--el-color-warning);
}

.is-slow {
  color: var(--el-color-danger);
}

.delete-form {
  margin-top: 18px;
}

.delete-form :deep(.el-date-editor) {
  width: 100%;
}

.drawer-heading {
  display: grid;
  gap: 4px;
}

.drawer-heading__eyebrow {
  font-size: 12px;
  color: var(--el-color-primary);
}

.drawer-heading strong {
  font-size: 18px;
  color: var(--el-text-color-primary);
}

.drawer-heading__meta {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.detail-summary {
  display: grid;
  grid-template-columns: 0.8fr 0.8fr 1.4fr;
  gap: 1px;
  margin-bottom: 16px;
  overflow: hidden;
  background: var(--el-border-color-lighter);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
}

.detail-summary__item {
  display: grid;
  gap: 8px;
  min-width: 0;
  padding: 14px;
  background: var(--el-bg-color);
}

.detail-summary__item > span {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.detail-summary__item strong {
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 14px;
  color: var(--el-text-color-primary);
  white-space: nowrap;
}

.detail-descriptions {
  word-break: break-all;
}

.detail-content {
  margin-top: 18px;
}

.detail-title {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 10px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

pre {
  max-height: 420px;
  padding: 16px;
  margin: 0;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-primary);
  word-break: break-word;
  white-space: pre-wrap;
  background: var(--el-fill-color-lighter);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
}

@media (width <= 1280px) {
  .audit-toolbar,
  .audit-toolbar__main {
    flex-wrap: wrap;
  }

  .audit-toolbar__summary {
    width: 100%;
  }
}

@media (width <= 768px) {
  .audit-toolbar__main {
    display: block;
  }

  .audit-primary-filter {
    margin-top: 12px;
  }

  .audit-date-filter,
  .audit-keyword-filter,
  .audit-user-filter {
    width: 100%;
  }

  .detail-summary {
    grid-template-columns: 1fr;
  }
}
</style>
