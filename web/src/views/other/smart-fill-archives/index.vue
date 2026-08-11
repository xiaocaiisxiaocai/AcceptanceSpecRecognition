<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import { Download, Refresh, Search } from "@element-plus/icons-vue";
import { ElMessage } from "element-plus";
import {
  downloadSmartFillArchive,
  getSmartFillArchiveList,
  type SmartFillArchiveListItem,
  type SmartFillArchiveListRequest
} from "@/api/execution-history";
import { getOrgUnitFlat, type OrgUnit } from "@/api/org-unit";
import { useUserStoreHook } from "@/store/modules/user";
import { triggerBrowserDownload } from "@/views/smart-fill/smartFillDownload.helpers";
import { formatExecutionHistoryDateTime } from "@/views/other/execution-history/executionHistory.formatters";

defineOptions({ name: "SmartFillArchives" });

const userStore = useUserStoreHook();
const isAdmin = computed(() => userStore.roleCode === "admin");
const loading = ref(false);
const downloadingId = ref<number>();
const records = ref<SmartFillArchiveListItem[]>([]);
const total = ref(0);
const orgUnits = ref<OrgUnit[]>([]);
const controller = ref<AbortController>();
const dateRange = ref<[Date, Date] | null>(null);

const filters = reactive({
  keyword: "",
  operatorKeyword: "",
  orgUnitId: undefined as number | undefined
});
const pagination = reactive({ page: 1, pageSize: 20 });

const pageSummary = computed(() => ({
  files: records.value.length,
  available: records.value.filter(item => item.hasResultArchive).length,
  adopted: records.value.reduce((sum, item) => sum + item.adoptedRowCount, 0),
  skipped: records.value.reduce((sum, item) => sum + item.skippedRowCount, 0)
}));

const buildRequest = (): SmartFillArchiveListRequest => ({
  page: pagination.page,
  pageSize: pagination.pageSize,
  keyword: filters.keyword.trim() || undefined,
  operatorKeyword: filters.operatorKeyword.trim() || undefined,
  orgUnitId: isAdmin.value ? filters.orgUnitId : undefined,
  from: dateRange.value?.[0].toISOString(),
  to: dateRange.value?.[1].toISOString()
});

const isCanceled = (error: unknown) =>
  error instanceof Error &&
  (error.name === "AbortError" ||
    error.name === "CanceledError" ||
    (error as Error & { code?: string }).code === "ERR_CANCELED");

const load = async () => {
  controller.value?.abort();
  const current = new AbortController();
  controller.value = current;
  loading.value = true;
  try {
    const response = await getSmartFillArchiveList(
      buildRequest(),
      current.signal
    );
    if (current.signal.aborted) return;
    if (response.code !== 0) {
      ElMessage.error(response.message || "加载填充存档失败");
      return;
    }
    records.value = response.data.items;
    total.value = response.data.total;
  } catch (error) {
    if (!isCanceled(error)) ElMessage.error("加载填充存档失败");
  } finally {
    if (controller.value === current) loading.value = false;
  }
};

const loadOrgUnits = async () => {
  if (!isAdmin.value) return;
  try {
    const response = await getOrgUnitFlat();
    if (response.code === 0) {
      orgUnits.value = (response.data ?? []).filter(
        item => item.isActive && item.unitType !== 0
      );
    }
  } catch {
    ElMessage.error("加载部门列表失败");
  }
};

const search = () => {
  pagination.page = 1;
  void load();
};

const reset = () => {
  filters.keyword = "";
  filters.operatorKeyword = "";
  filters.orgUnitId = undefined;
  dateRange.value = null;
  pagination.page = 1;
  void load();
};

const download = async (row: SmartFillArchiveListItem) => {
  if (!row.hasResultArchive || downloadingId.value) return;
  downloadingId.value = row.id;
  try {
    const blob = await downloadSmartFillArchive(row.id);
    triggerBrowserDownload(blob, row.resultFileName || row.sourceFileName);
  } catch (error) {
    const message = error instanceof Error ? error.message : "";
    ElMessage.error(message || "下载失败，存档可能已缺失或损坏");
  } finally {
    downloadingId.value = undefined;
  }
};

const formatSize = (bytes?: number) => {
  if (!bytes && bytes !== 0) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
};

const fileType = (row: SmartFillArchiveListItem) =>
  row.sourceFileType === 1 ? "Excel" : "Word";

onMounted(() => {
  void load();
  void loadOrgUnits();
});

onBeforeUnmount(() => controller.value?.abort());
</script>

<template>
  <div class="page page--fill archive-page">
    <section class="archive-panel" aria-label="填充存档">
      <header class="archive-toolbar">
        <div class="archive-title">
          <h2>填充存档</h2>
          <span>保留 365 天</span>
        </div>
        <el-form :inline="true" class="archive-filters">
          <el-form-item label="来源文件">
            <el-input
              v-model="filters.keyword"
              clearable
              placeholder="文件名 / 任务ID"
              @keyup.enter="search"
            />
          </el-form-item>
          <el-form-item label="执行时间">
            <el-date-picker
              v-model="dateRange"
              type="datetimerange"
              range-separator="至"
              start-placeholder="开始时间"
              end-placeholder="结束时间"
            />
          </el-form-item>
          <el-form-item v-if="isAdmin" label="所属部门">
            <el-select
              v-model="filters.orgUnitId"
              clearable
              filterable
              placeholder="全部部门"
            >
              <el-option
                v-for="org in orgUnits"
                :key="org.id"
                :label="org.name"
                :value="org.id"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="操作人">
            <el-input
              v-model="filters.operatorKeyword"
              clearable
              placeholder="姓名 / 账号"
              @keyup.enter="search"
            />
          </el-form-item>
          <el-form-item class="filter-actions">
            <el-button type="primary" :icon="Search" @click="search">
              查询
            </el-button>
            <el-button :icon="Refresh" @click="reset">重置</el-button>
          </el-form-item>
        </el-form>
      </header>

      <div class="summary-bar" aria-label="当前页摘要">
        <span class="summary-label">当前页</span>
        <span
          ><strong>{{ pageSummary.files }}</strong> 份</span
        >
        <span
          ><strong>{{ pageSummary.available }}</strong> 份可下载</span
        >
        <span
          ><strong>{{ pageSummary.adopted }}</strong> 行已填充</span
        >
        <span
          ><strong>{{ pageSummary.skipped }}</strong> 行跳过</span
        >
      </div>

      <el-table
        v-loading="loading"
        :data="records"
        class="archive-table"
        height="100%"
        row-key="id"
        empty-text="暂无填充存档"
      >
        <el-table-column type="expand" width="42">
          <template #default="{ row }">
            <div class="row-summary">
              <span>任务ID：{{ row.taskId }}</span>
              <span>总行数：{{ row.totalRowCount }}</span>
              <span>已填充：{{ row.adoptedRowCount }}</span>
              <span>未匹配：{{ row.unmatchedRowCount }}</span>
              <span>跳过：{{ row.skippedRowCount }}</span>
              <span>存档大小：{{ formatSize(row.resultFileSizeBytes) }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column
          prop="sourceFileName"
          label="来源文件"
          min-width="240"
          show-overflow-tooltip
        >
          <template #default="{ row }">
            <div class="file-cell">
              <span class="file-name">{{ row.sourceFileName }}</span>
              <span>{{ fileType(row) }}</span>
            </div>
          </template>
        </el-table-column>
        <el-table-column
          prop="ownerOrgUnitName"
          label="所属部门"
          min-width="130"
        >
          <template #default="{ row }">{{
            row.ownerOrgUnitName || "-"
          }}</template>
        </el-table-column>
        <el-table-column
          prop="createdByDisplayName"
          label="操作人"
          min-width="120"
        >
          <template #default="{ row }">{{
            row.createdByDisplayName || "-"
          }}</template>
        </el-table-column>
        <el-table-column label="结果" min-width="150">
          <template #default="{ row }">
            <span class="result-count">{{ row.adoptedRowCount }} 行</span>
            <span class="result-muted"> / 跳过 {{ row.skippedRowCount }}</span>
          </template>
        </el-table-column>
        <el-table-column label="存档状态" width="110">
          <template #default="{ row }">
            <el-tag v-if="row.hasResultArchive" type="success" effect="plain"
              >可下载</el-tag
            >
            <el-tag v-else type="info" effect="plain">无结果文件</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="执行时间" width="172">
          <template #default="{ row }">{{
            formatExecutionHistoryDateTime(row.createdAt)
          }}</template>
        </el-table-column>
        <el-table-column label="操作" width="92" fixed="right" align="center">
          <template #default="{ row }">
            <el-tooltip
              :content="
                row.hasResultArchive ? '下载结果文件' : '该历史记录没有结果文件'
              "
            >
              <span>
                <el-button
                  circle
                  :icon="Download"
                  :disabled="!row.hasResultArchive"
                  :loading="downloadingId === row.id"
                  aria-label="下载结果文件"
                  @click="download(row)"
                />
              </span>
            </el-tooltip>
          </template>
        </el-table-column>
      </el-table>

      <footer class="archive-footer">
        <span>共 {{ total }} 份存档</span>
        <el-pagination
          v-model:current-page="pagination.page"
          v-model:page-size="pagination.pageSize"
          :page-sizes="[20, 50, 100, 200]"
          :total="total"
          background
          layout="sizes, prev, pager, next"
          @current-change="load"
          @size-change="search"
        />
      </footer>
    </section>
  </div>
</template>

<style scoped>
.archive-page {
  min-height: 0;
}

.archive-panel {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
}

.archive-toolbar {
  padding: 16px 18px 4px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.archive-title {
  display: flex;
  gap: 10px;
  align-items: baseline;
  margin-bottom: 14px;
}

.archive-title h2 {
  margin: 0;
  font-size: 18px;
  font-weight: 650;
  color: var(--el-text-color-primary);
  letter-spacing: 0;
}

.archive-title span,
.archive-footer,
.result-muted {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.archive-filters {
  display: flex;
  flex-wrap: wrap;
  gap: 0 12px;
}

.archive-filters :deep(.el-form-item) {
  margin-right: 0;
  margin-bottom: 12px;
}

.archive-filters :deep(.el-input) {
  width: 190px;
}

.archive-filters :deep(.el-select) {
  width: 160px;
}

.summary-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 20px;
  align-items: center;
  min-height: 42px;
  padding: 8px 18px;
  font-size: 13px;
  color: var(--el-text-color-regular);
  background: var(--el-fill-color-extra-light);
}

.summary-bar strong {
  color: var(--el-color-primary);
}

.summary-label {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.archive-table {
  flex: 1;
  min-height: 260px;
}

.file-cell {
  display: flex;
  gap: 8px;
  align-items: center;
  min-width: 0;
}

.file-cell > span:last-child {
  flex: none;
  padding: 1px 5px;
  font-size: 11px;
  color: var(--el-text-color-secondary);
  background: var(--el-fill-color-light);
  border-radius: 3px;
}

.file-name {
  overflow: hidden;
  text-overflow: ellipsis;
  font-weight: 550;
  white-space: nowrap;
}

.result-count {
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.row-summary {
  display: flex;
  flex-wrap: wrap;
  gap: 8px 24px;
  padding: 4px 52px 8px;
  font-size: 13px;
  color: var(--el-text-color-regular);
}

.archive-footer {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  min-height: 54px;
  padding: 8px 18px;
  border-top: 1px solid var(--el-border-color-lighter);
}

@media (width <= 900px) {
  .archive-toolbar {
    padding-inline: 12px;
  }

  .archive-filters,
  .archive-filters :deep(.el-form-item),
  .archive-filters :deep(.el-input),
  .archive-filters :deep(.el-select),
  .archive-filters :deep(.el-date-editor) {
    width: 100%;
  }

  .filter-actions :deep(.el-form-item__content) {
    justify-content: flex-end;
  }

  .summary-bar {
    gap: 10px 16px;
    padding-inline: 12px;
  }

  .archive-footer {
    justify-content: center;
    padding-inline: 8px;
  }

  .row-summary {
    padding-inline: 24px;
  }
}

@media (width <= 520px) {
  .archive-footer :deep(.el-pagination__sizes) {
    display: none;
  }
}
</style>
