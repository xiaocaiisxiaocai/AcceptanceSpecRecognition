<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
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
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "AuditLogs"
});

const loading = ref(false);
const deleting = ref(false);
const tableData = ref<AuditLogListItem[]>([]);
const total = ref(0);
const queryRange = ref<string[]>([]);
const deleteRange = ref<string[]>([]);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  source: AuditLogSource.BackendRequest as AuditLogSource | undefined,
  level: undefined as AuditLogLevel | undefined,
  username: "",
  requestMethod: "",
  keyword: ""
});

const sourceOptions = [{ label: "控制器操作", value: AuditLogSource.BackendRequest }];

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
  if (!ensurePermission("btn:audit-log:delete-range", "权限不足，无法删除审计日志")) {
    return;
  }
  const [from, to] = deleteRange.value ?? [];
  if (!from && !to) {
    ElMessage.warning("请选择删除时间范围");
    return;
  }

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
  try {
    const res = await deleteAuditLogsByRange({
      from: from || undefined,
      to: to || undefined
    });
    if (res.code === 0) {
      ElMessage.success(res.message || "删除成功");
      deleteRange.value = [];
      queryParams.page = 1;
      await loadData();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch {
    ElMessage.error("删除失败");
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
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">审计日志</div>
        <div class="page-subtitle">仅记录控制器增删改动作，不记录查询请求</div>
      </div>
    </div>

    <el-card class="toolbar-card">
      <el-form :inline="true">
        <el-form-item label="来源">
          <el-select
            v-model="queryParams.source"
            clearable
            placeholder="全部"
            class="search-select search-select--300"
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
            class="search-select search-select--300"
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
            class="search-select search-select--300"
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

      <el-form v-if="canDeleteRange" :inline="true" class="delete-row">
        <el-form-item label="删除时间">
          <el-date-picker
            v-model="deleteRange"
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
    </el-card>

    <el-card class="audit-table-card">
      <template #header>
        <div class="flex justify-between items-center">
          <span>审计日志</span>
          <span class="text-sm text-gray-500">详情可点击“查看”展开</span>
        </div>
      </template>

      <div class="table-wrap">
        <el-table v-loading="loading" :data="tableData" stripe height="100%">
          <el-table-column prop="id" label="ID" width="80" />
          <el-table-column label="级别" width="100">
            <template #default="{ row }">
              <el-tag :type="getLevelType(row.level)">
                {{ getLevelLabel(row.level) }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="eventType" label="事件" width="150" />
          <el-table-column prop="username" label="用户" width="120" />
          <el-table-column prop="requestMethod" label="方法" width="90" />
          <el-table-column
            prop="requestPath"
            label="请求路径"
            min-width="220"
            show-overflow-tooltip
          />
          <el-table-column
            prop="frontendRoute"
            label="前端路由"
            min-width="180"
            show-overflow-tooltip
          />
          <el-table-column prop="statusCode" label="状态" width="90" />
          <el-table-column prop="durationMs" label="耗时(ms)" width="100" />
          <el-table-column prop="createdAt" label="时间" width="180">
            <template #default="{ row }">
              {{ new Date(row.createdAt).toLocaleString() }}
            </template>
          </el-table-column>
          <el-table-column label="详情" width="90" fixed="right">
            <template #default="{ row }">
              <el-button type="primary" link @click="openDetail(row)">查看</el-button>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="pager-wrap">
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
.page {
  padding: 24px;
  height: calc(100vh - 104px);
  display: flex;
  flex-direction: column;
  gap: 12px;
  overflow: hidden;
}

.toolbar-card {
  flex-shrink: 0;
}

.delete-row {
  padding-top: 8px;
}

.audit-table-card {
  flex: 1;
  min-height: 0;
}

.audit-table-card :deep(.el-card__body) {
  height: calc(100% - 0px);
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.table-wrap {
  flex: 1;
  min-height: 0;
}

.pager-wrap {
  padding-top: 12px;
  display: flex;
  justify-content: flex-end;
  flex-shrink: 0;
}

.detail-content {
  margin-top: 12px;
}

.detail-title {
  margin-bottom: 8px;
  color: var(--el-text-color-primary);
  font-weight: 600;
}

pre {
  max-height: 320px;
  margin: 0;
  padding: 12px;
  overflow: auto;
  border-radius: 6px;
  background: var(--el-fill-color-light);
  color: var(--el-text-color-primary);
  font-size: 12px;
  line-height: 1.5;
}
</style>
