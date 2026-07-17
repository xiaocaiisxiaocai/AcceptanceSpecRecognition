<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
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
const tableData = ref<AuditLogListItem[]>([]);
const total = ref(0);
const queryRange = ref<string[]>([]);
const deleteForm = reactive({ range: [] as string[] });
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
  { label: "控制器操作", value: AuditLogSource.BackendRequest }
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
const canDeleteRange = computed(() => hasPerms("btn:audit-log:delete-range"));

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
      from: from || undefined,
      to: to || undefined
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
      from: from || undefined,
      to: to || undefined
    });
    if (res.code === 0) {
      ElMessage.success(res.message || "删除成功");
      deleteForm.range = [];
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
    <div class="page-header">
      <div>
        <div class="page-title">审计日志</div>
      </div>
    </div>

    <el-card class="table-card audit-table-card" shadow="never">
      <template #header>
        <div class="list-card-toolbar">
          <div class="list-card-toolbar__right">
            <el-form :inline="true" class="filter-form audit-filter-form">
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

              <el-form-item label="查询时间">
                <el-date-picker
                  v-model="queryRange"
                  class="audit-date-filter"
                  type="datetimerange"
                  unlink-panels
                  value-format="YYYY-MM-DDTHH:mm:ss"
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
              </el-form-item>
            </el-form>
          </div>
        </div>
      </template>

      <el-form
        v-if="canDeleteRange"
        ref="deleteFormRef"
        :model="deleteForm"
        :rules="deleteFormRules"
        :inline="true"
        class="delete-row filter-form"
      >
        <el-form-item label="删除时间" prop="range">
          <el-date-picker
            v-model="deleteForm.range"
            type="datetimerange"
            unlink-panels
            value-format="YYYY-MM-DDTHH:mm:ss"
            start-placeholder="开始时间"
            end-placeholder="结束时间"
          />
        </el-form-item>
        <el-form-item>
          <el-button
            type="danger"
            plain
            :loading="deleting"
            @click="handleDeleteByRange"
          >
            按时间范围删除
          </el-button>
        </el-form-item>
      </el-form>

      <div class="table-region">
        <el-table v-loading="loading" :data="tableData" stripe height="100%">
          <el-table-column prop="id" label="ID" width="80" />
          <el-table-column label="级别" width="min(100px, calc(100vw - 32px))">
            <template #default="{ row }">
              <el-tag :type="getLevelType(row.level)">
                {{ getLevelLabel(row.level) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column
            prop="eventType"
            label="事件"
            width="min(150px, calc(100vw - 32px))"
          />
          <el-table-column
            prop="username"
            label="用户"
            width="min(120px, calc(100vw - 32px))"
          />
          <el-table-column prop="requestMethod" label="方法" width="90" />
          <el-table-column
            prop="requestPath"
            label="请求路径"
            min-width="min(220px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column
            prop="frontendRoute"
            label="前端路由"
            min-width="min(180px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column prop="statusCode" label="状态" width="90" />
          <el-table-column
            prop="durationMs"
            label="耗时(ms)"
            width="min(100px, calc(100vw - 32px))"
          />
          <el-table-column
            prop="createdAt"
            label="时间"
            width="min(180px, calc(100vw - 32px))"
          >
            <template #default="{ row }">
              {{ new Date(row.createdAt).toLocaleString() }}
            </template>
          </el-table-column>
          <el-table-column label="详情" width="90" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" link @click="openDetail(row)"
                >查看</el-button
              >
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
      v-model="detailVisible"
      title="审计详情"
      width="760px"
      append-to-body
      destroy-on-close
    >
      <div v-loading="detailLoading">
        <el-descriptions :column="2" border>
          <el-descriptions-item label="ID">
            {{ currentDetail?.id }}
          </el-descriptions-item>
          <el-descriptions-item label="事件">
            {{ currentDetail?.eventType }}
          </el-descriptions-item>
          <el-descriptions-item label="用户">
            {{ currentDetail?.username || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="时间">
            {{
              currentDetail
                ? new Date(currentDetail.createdAt).toLocaleString()
                : "-"
            }}
          </el-descriptions-item>
          <el-descriptions-item label="请求路径" :span="2">
            {{ currentDetail?.requestPath || "-" }}
          </el-descriptions-item>
        </el-descriptions>

        <div class="detail-content">
          <div class="detail-title">详情内容</div>
          <pre>{{ formattedDetails }}</pre>
        </div>
      </div>
    </el-dialog>
  </div>
</template>

<style scoped>
.delete-row {
  padding-top: 8px;
}

.audit-filter-form {
  flex: 1;
  flex-wrap: nowrap;
  min-width: 0;
  min-height: 0;
}

.audit-user-filter {
  width: 130px;
}

.audit-date-filter {
  width: 300px;
}

.audit-keyword-filter {
  width: 180px;
}

.detail-content {
  margin-top: 12px;
}

.detail-title {
  margin-bottom: 8px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

pre {
  max-height: 320px;
  padding: 12px;
  margin: 0;
  overflow: auto;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-primary);
  background: var(--el-fill-color-light);
  border-radius: 6px;
}

@media (width <= 1750px) {
  .audit-filter-form {
    flex-wrap: wrap;
  }
}
</style>
