<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import {
  getExecutionHistoryDetail,
  getExecutionHistoryList,
  type ExecutionHistoryDetail,
  type ExecutionHistoryFile,
  type ExecutionHistoryListItem,
  type ExecutionHistorySheet
} from "@/api/execution-history";

defineOptions({
  name: "ExecutionHistory"
});

const loading = ref(false);
const detailLoading = ref(false);
const tableData = ref<ExecutionHistoryListItem[]>([]);
const total = ref(0);
const currentDetail = ref<ExecutionHistoryDetail | null>(null);
const selectedFileIndex = ref(0);
const selectedSheetName = ref("");

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: "",
  taskType: ""
});

const taskTypeOptions = [
  { label: "全部", value: "" },
  { label: "智能填充", value: "smart-fill" },
  { label: "批量回复", value: "batch-reply" }
];

const currentFile = computed<ExecutionHistoryFile | null>(() => {
  const files = currentDetail.value?.files ?? [];
  return files[selectedFileIndex.value] ?? null;
});

const currentSheet = computed<ExecutionHistorySheet | null>(() => {
  const sheets = currentFile.value?.sheets ?? [];
  return sheets.find(sheet => sheet.sheetName === selectedSheetName.value) ?? sheets[0] ?? null;
});

watch(currentFile, file => {
  const firstSheetName = file?.sheets?.[0]?.sheetName ?? "";
  if (file && !file.sheets.some(sheet => sheet.sheetName === selectedSheetName.value)) {
    selectedSheetName.value = firstSheetName;
  }
});

const getTaskTypeText = (taskType: string) => {
  return taskType === "batch-reply" ? "批量回复" : "智能填充";
};

const getStatusText = (status: string) => {
  switch (status) {
    case "adopted":
      return "已采用";
    case "skipped":
      return "已跳过";
    case "not-adopted":
      return "未采用";
    case "unmatched":
    default:
      return "未匹配";
  }
};

const getStatusType = (status: string) => {
  switch (status) {
    case "adopted":
      return "success";
    case "skipped":
      return "info";
    case "not-adopted":
      return "warning";
    case "unmatched":
    default:
      return "danger";
  }
};

const formatConfidence = (confidencePercent: number) =>
  `${(confidencePercent ?? 0).toFixed(1)}%`;

const loadList = async () => {
  loading.value = true;
  try {
    const res = await getExecutionHistoryList({
      page: queryParams.page,
      pageSize: queryParams.pageSize,
      keyword: queryParams.keyword || undefined,
      taskType: queryParams.taskType || undefined
    });

    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;

      if (res.data.items.length > 0 && !currentDetail.value) {
        await openDetail(res.data.items[0]);
      }
    } else {
      ElMessage.error(res.message || "加载执行记录失败");
    }
  } catch {
    ElMessage.error("加载执行记录失败");
  } finally {
    loading.value = false;
  }
};

const openDetail = async (row: ExecutionHistoryListItem) => {
  detailLoading.value = true;
  try {
    const res = await getExecutionHistoryDetail(row.id);
    if (res.code === 0) {
      currentDetail.value = res.data;
      selectedFileIndex.value = 0;
      selectedSheetName.value = res.data.files[0]?.sheets[0]?.sheetName ?? "";
    } else {
      ElMessage.error(res.message || "加载详情失败");
    }
  } catch {
    ElMessage.error("加载详情失败");
  } finally {
    detailLoading.value = false;
  }
};

const handleSearch = () => {
  queryParams.page = 1;
  loadList();
};

const handleReset = () => {
  queryParams.page = 1;
  queryParams.pageSize = 20;
  queryParams.keyword = "";
  queryParams.taskType = "";
  loadList();
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadList();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadList();
};

onMounted(loadList);
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">执行记录</div>
        <div class="page-subtitle">
          按任务查看智能填充与批量回复结果，详情按文件和 Sheet 展示逐行记录
        </div>
      </div>
    </div>

    <el-card class="toolbar-card">
      <el-form :inline="true">
        <el-form-item label="任务类型">
          <el-select v-model="queryParams.taskType" class="search-select search-select--300">
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
    </el-card>

    <div class="history-layout">
      <el-card class="history-list-card">
        <template #header>
          <div class="card-header">
            <span>任务列表</span>
            <span class="card-header-tip">一次执行一条记录</span>
          </div>
        </template>

        <div class="table-wrap">
          <el-table v-loading="loading" :data="tableData" stripe height="100%">
            <el-table-column prop="taskType" label="类型" width="100">
              <template #default="{ row }">
                {{ getTaskTypeText(row.taskType) }}
              </template>
            </el-table-column>
            <el-table-column prop="sourceFileName" label="来源文件" min-width="220" show-overflow-tooltip />
            <el-table-column prop="fileCount" label="文件" width="70" />
            <el-table-column prop="totalRowCount" label="总行数" width="90" />
            <el-table-column prop="adoptedRowCount" label="已采用" width="90" />
            <el-table-column prop="unmatchedRowCount" label="未匹配" width="90" />
            <el-table-column prop="skippedRowCount" label="已跳过" width="90" />
            <el-table-column prop="notAdoptedRowCount" label="未采用" width="90" />
            <el-table-column prop="manualSelectedRowCount" label="人工选择" width="100" />
            <el-table-column prop="createdAt" label="时间" width="180">
              <template #default="{ row }">
                {{ new Date(row.createdAt).toLocaleString() }}
              </template>
            </el-table-column>
            <el-table-column label="查看" width="90" fixed="right">
              <template #default="{ row }">
                <el-button type="primary" link @click="openDetail(row)">查看</el-button>
              </template>
            </el-table-column>
          </el-table>
        </div>

        <div class="pager-wrap">
          <el-pagination
            background
            layout="total, sizes, prev, pager, next"
            :total="total"
            :page-size="queryParams.pageSize"
            :current-page="queryParams.page"
            @current-change="handlePageChange"
            @size-change="handleSizeChange"
          />
        </div>
      </el-card>

      <el-card class="history-detail-card" v-loading="detailLoading">
        <template #header>
          <div class="card-header">
            <span>任务详情</span>
            <span v-if="currentDetail" class="card-header-tip">
              {{ currentDetail.sourceFileName }} / {{ getTaskTypeText(currentDetail.taskType) }}
            </span>
          </div>
        </template>

        <template v-if="currentDetail">
          <el-descriptions :column="4" border size="small" class="detail-summary">
            <el-descriptions-item label="来源文件">
              {{ currentDetail.sourceFileName }}
            </el-descriptions-item>
            <el-descriptions-item label="类型">
              {{ getTaskTypeText(currentDetail.taskType) }}
            </el-descriptions-item>
            <el-descriptions-item label="文件数">
              {{ currentDetail.fileCount }}
            </el-descriptions-item>
            <el-descriptions-item label="总行数">
              {{ currentDetail.totalRowCount }}
            </el-descriptions-item>
          </el-descriptions>

          <div class="file-list">
            <span class="section-title">文件</span>
            <el-segmented
              v-model="selectedFileIndex"
              :options="
                currentDetail.files.map((file, index) => ({
                  label: file.fileName,
                  value: index
                }))
              "
            />
          </div>

          <el-tabs v-model="selectedSheetName" class="sheet-tabs">
            <el-tab-pane
              v-for="sheet in currentFile?.sheets ?? []"
              :key="sheet.sheetName"
              :label="sheet.sheetName || `Sheet ${sheet.sheetIndex + 1}`"
              :name="sheet.sheetName"
            >
              <el-table :data="currentSheet?.rows ?? []" stripe border max-height="520">
                <el-table-column label="行号" width="80">
                  <template #default="{ row }">
                    {{ row.rowIndex + 1 }}
                  </template>
                </el-table-column>
                <el-table-column prop="project" label="项目" min-width="160" show-overflow-tooltip />
                <el-table-column prop="specification" label="规格" min-width="180" show-overflow-tooltip />
                <el-table-column label="匹配结果" min-width="180" show-overflow-tooltip>
                  <template #default="{ row }">
                    {{ row.matchedSpecification || row.matchedProject || "-" }}
                  </template>
                </el-table-column>
                <el-table-column prop="acceptance" label="验收" min-width="180" show-overflow-tooltip />
                <el-table-column prop="remark" label="备注" min-width="140" show-overflow-tooltip />
                <el-table-column label="置信度" width="100">
                  <template #default="{ row }">
                    {{ formatConfidence(row.confidencePercent) }}
                  </template>
                </el-table-column>
                <el-table-column label="状态" width="100">
                  <template #default="{ row }">
                    <el-tag :type="getStatusType(row.status)">
                      {{ getStatusText(row.status) }}
                    </el-tag>
                  </template>
                </el-table-column>
                <el-table-column label="人工选择" width="100">
                  <template #default="{ row }">
                    {{ row.isManualSelected ? "是" : "否" }}
                  </template>
                </el-table-column>
              </el-table>
            </el-tab-pane>
          </el-tabs>
        </template>

        <el-empty v-else description="暂无执行记录详情" />
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.history-layout {
  display: grid;
  grid-template-columns: minmax(420px, 1fr) minmax(540px, 1.3fr);
  gap: 16px;
  align-items: start;
}

.history-list-card,
.history-detail-card {
  min-height: 720px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.card-header-tip {
  color: #6b7280;
  font-size: 12px;
}

.detail-summary {
  margin-bottom: 16px;
}

.file-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 16px;
}

.section-title {
  font-size: 13px;
  font-weight: 600;
  color: #374151;
}

.table-wrap {
  height: 620px;
}

.pager-wrap {
  margin-top: 16px;
  display: flex;
  justify-content: flex-end;
}

.sheet-tabs :deep(.el-tabs__content) {
  padding-top: 8px;
}

@media (max-width: 1400px) {
  .history-layout {
    grid-template-columns: 1fr;
  }
}
</style>
