<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type {
  ExecutionHistoryFile,
  ExecutionHistoryRow,
  ExecutionHistorySheet
} from "@/api/execution-history";

const props = defineProps<{
  files: ExecutionHistoryFile[];
}>();

const selectedTabKey = ref("");
const statusFilter = ref("");
const keyword = ref("");
const page = ref(1);
const pageSize = ref(50);

const statusOptions = [
  { label: "全部", value: "" },
  { label: "已采用", value: "adopted" },
  { label: "未采用", value: "not-adopted" },
  { label: "未匹配", value: "unmatched" },
  { label: "已跳过", value: "skipped" }
];

const tabItems = computed(() =>
  props.files.flatMap((file, fileIndex) =>
    file.sheets.map(sheet => {
      const sheetLabel = sheet.sheetName || `Sheet ${sheet.sheetIndex + 1}`;

      return {
        key: `${fileIndex}:${sheet.sheetIndex}`,
        label:
          props.files.length > 1
            ? `${file.fileName} / ${sheetLabel}`
            : sheetLabel,
        sheet
      };
    })
  )
);

const currentSheet = computed<ExecutionHistorySheet | null>(
  () =>
    tabItems.value.find(item => item.key === selectedTabKey.value)?.sheet ??
    tabItems.value[0]?.sheet ??
    null
);

const filteredRows = computed<ExecutionHistoryRow[]>(() => {
  const rows = currentSheet.value?.rows ?? [];
  const search = keyword.value.trim().toLowerCase();

  return rows.filter(row => {
    if (statusFilter.value && row.status !== statusFilter.value) {
      return false;
    }

    if (!search) return true;

    return [
      `${row.rowIndex + 1}`,
      row.project,
      row.specification,
      row.acceptance,
      row.remark
    ]
      .filter(Boolean)
      .some(value => String(value).toLowerCase().includes(search));
  });
});

const pagedRows = computed(() => {
  const start = (page.value - 1) * pageSize.value;
  return filteredRows.value.slice(start, start + pageSize.value);
});

const resetResultPage = () => {
  page.value = 1;
};

watch(
  () => props.files,
  () => {
    selectedTabKey.value = tabItems.value[0]?.key ?? "";
    resetResultPage();
  },
  { immediate: true }
);

watch(tabItems, items => {
  if (!items.some(item => item.key === selectedTabKey.value)) {
    selectedTabKey.value = items[0]?.key ?? "";
  }
});

watch([currentSheet, statusFilter, keyword, pageSize], () => {
  resetResultPage();
});

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
</script>

<template>
  <div class="result-table">
    <template v-if="files.length > 0">
      <el-tabs v-model="selectedTabKey" class="result-tabs">
        <el-tab-pane
          v-for="item in tabItems"
          :key="item.key"
          :label="item.label"
          :name="item.key"
        />
      </el-tabs>

      <div class="result-toolbar">
        <el-form :inline="true" class="filter-form">
          <el-form-item label="状态">
            <el-select
              v-model="statusFilter"
              class="search-select search-select--160"
            >
              <el-option
                v-for="item in statusOptions"
                :key="item.value"
                :label="item.label"
                :value="item.value"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="关键词">
            <el-input
              v-model="keyword"
              clearable
              placeholder="行号/项目/规格/验收/备注"
            />
          </el-form-item>
        </el-form>
      </div>

      <div class="result-table__body">
        <el-table :data="pagedRows" stripe border height="100%">
          <el-table-column label="行号" width="80">
            <template #default="{ row }">
              {{ row.rowIndex + 1 }}
            </template>
          </el-table-column>
          <el-table-column
            prop="project"
            label="项目"
            min-width="min(140px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column
            prop="specification"
            label="规格"
            min-width="min(180px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column
            prop="acceptance"
            label="验收"
            min-width="min(180px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column
            prop="remark"
            label="备注"
            min-width="min(160px, calc(100vw - 32px))"
            show-overflow-tooltip
          />
          <el-table-column
            label="置信度"
            width="min(100px, calc(100vw - 32px))"
          >
            <template #default="{ row }">
              {{ formatConfidence(row.confidencePercent) }}
            </template>
          </el-table-column>
          <el-table-column label="状态" width="min(100px, calc(100vw - 32px))">
            <template #default="{ row }">
              <el-tag :type="getStatusType(row.status)">
                {{ getStatusText(row.status) }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="result-pagination">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          background
          layout="total, sizes, prev, pager, next"
          :page-sizes="[20, 50, 100, 200]"
          :total="filteredRows.length"
        />
      </div>
    </template>

    <el-empty v-else description="暂无可展示的结果行" />
  </div>
</template>

<style scoped>
.result-table {
  display: flex;
  flex: 1;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.result-toolbar {
  display: flex;
  flex-shrink: 0;
}

.result-tabs {
  flex-shrink: 0;
}

.result-tabs :deep(.el-tabs__header) {
  margin: 0;
}

.result-tabs :deep(.el-tabs__content) {
  display: none;
}

.result-table__body {
  flex: 1 1 0;
  min-height: 0;
}

.result-pagination {
  display: flex;
  flex-shrink: 0;
  justify-content: flex-end;
}
</style>
