<script setup lang="ts">
import { computed, onBeforeUnmount, reactive, ref, watch } from "vue";
import type {
  ExecutionHistoryDetail,
  ExecutionHistorySmartFillFile,
  ExecutionHistorySmartFillRow,
  ExecutionHistorySmartFillSheet
} from "@/api/execution-history";
import { getExecutionHistorySmartFillRow } from "@/api/execution-history";
import { createExecutionHistoryRequestGate } from "../useExecutionHistoryRequests";
import ExecutionHistorySmartFillRowDetail from "./ExecutionHistorySmartFillRowDetail.vue";

const props = defineProps<{
  detail: ExecutionHistoryDetail;
}>();

const selectedTabKey = ref("");
const statusFilter = ref("");
const keyword = ref("");
const page = ref(1);
const pageSize = ref(50);
const rowRequestGate = createExecutionHistoryRequestGate();
const activeRowRequestKey = ref("");
const selectedRowKey = ref("");
const selectedSummaryRow = ref<ExecutionHistorySmartFillRow>();

interface RowDetailState {
  loading: boolean;
  row?: ExecutionHistorySmartFillRow;
  errorMessage?: string;
}

const rowDetailCache = reactive(new Map<string, RowDetailState>());

const statusOptions = [
  { label: "全部", value: "" },
  { label: "已采用", value: "adopted" },
  { label: "未采用", value: "not-adopted" },
  { label: "未匹配", value: "unmatched" },
  { label: "已跳过", value: "skipped" }
];

const playback = computed(() => props.detail.smartFillPlayback);
const files = computed<ExecutionHistorySmartFillFile[]>(
  () => playback.value?.files ?? []
);

const tabItems = computed(() =>
  files.value.flatMap((file, fileIndex) =>
    file.sheets.map((sheet, sheetIndex) => {
      const sheetLabel = sheet.sheetName || `Sheet ${sheet.sheetIndex + 1}`;

      return {
        key: `${fileIndex}:${sheetIndex}`,
        label:
          files.value.length > 1
            ? `${file.fileName} / ${sheetLabel}`
            : sheetLabel,
        sheet,
        fileIndex,
        sheetIndex
      };
    })
  )
);

const currentTab = computed(
  () =>
    tabItems.value.find(item => item.key === selectedTabKey.value) ??
    tabItems.value[0]
);

const currentSheet = computed<ExecutionHistorySmartFillSheet | null>(
  () => currentTab.value?.sheet ?? null
);
const selectedRowDetailState = computed(() =>
  selectedRowKey.value ? rowDetailCache.get(selectedRowKey.value) : undefined
);
const hasPlaybackArchive = computed(
  () =>
    props.detail.smartFillSummary?.hasPlaybackArchive === true ||
    playback.value?.hasFullArchive === true
);

const filteredRows = computed<ExecutionHistorySmartFillRow[]>(() => {
  const rows = currentSheet.value?.rows ?? [];
  const search = keyword.value.trim().toLowerCase();

  return rows.filter(row => {
    if (statusFilter.value && row.status !== statusFilter.value) {
      return false;
    }

    if (!search) return true;

    return [
      `${row.rowIndex + 1}`,
      row.sourceProject,
      row.sourceSpecification,
      row.executionSnapshot.finalAcceptance,
      row.executionSnapshot.finalRemark,
      row.displayTags.join(" ")
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
  files,
  () => {
    selectedTabKey.value = tabItems.value[0]?.key ?? "";
    resetResultPage();
  },
  { immediate: true }
);

watch(
  () => props.detail.id,
  () => {
    rowRequestGate.cancel();
    activeRowRequestKey.value = "";
    rowDetailCache.clear();
    selectedRowKey.value = "";
    selectedSummaryRow.value = undefined;
  }
);

watch(selectedTabKey, () => {
  rowRequestGate.cancel();
  activeRowRequestKey.value = "";
  selectedRowKey.value = "";
  selectedSummaryRow.value = undefined;
});

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

const getMatchOriginText = (matchOrigin: string) => {
  switch (matchOrigin) {
    case "exact":
      return "完全匹配";
    case "ai":
      return "AI匹配";
    default:
      return "未匹配";
  }
};

const getRowCacheKey = (
  fileIndex: number,
  sheetIndex: number,
  rowIndex: number
) => `${props.detail.id}:${fileIndex}:${sheetIndex}:${rowIndex}`;

const loadRowDetail = async (
  row: ExecutionHistorySmartFillRow,
  force = false
) => {
  const tab = currentTab.value;
  if (!tab) return;

  const params = {
    fileIndex: tab.fileIndex,
    sheetIndex: tab.sheetIndex,
    rowIndex: row.rowIndex
  };
  const cacheKey = getRowCacheKey(
    params.fileIndex,
    params.sheetIndex,
    params.rowIndex
  );
  selectedRowKey.value = cacheKey;
  selectedSummaryRow.value = row;

  const cached = rowDetailCache.get(cacheKey);
  if (!force && cached?.loading && activeRowRequestKey.value === cacheKey) {
    return;
  }
  if (!force && cached?.row) {
    if (activeRowRequestKey.value && activeRowRequestKey.value !== cacheKey) {
      rowRequestGate.cancel();
      activeRowRequestKey.value = "";
    }
    return;
  }

  const request = rowRequestGate.begin(cacheKey);
  activeRowRequestKey.value = cacheKey;
  rowDetailCache.set(cacheKey, { loading: true });

  try {
    const response = await getExecutionHistorySmartFillRow(
      props.detail.id,
      params,
      request.signal
    );
    if (!request.isCurrent() || selectedRowKey.value !== cacheKey) return;

    if (response.code !== 0) {
      throw new Error(response.message || "完整逐行回放加载失败");
    }

    rowDetailCache.set(cacheKey, {
      loading: false,
      row: response.data
    });
  } catch {
    if (!request.isCurrent() || selectedRowKey.value !== cacheKey) return;

    rowDetailCache.set(cacheKey, {
      loading: false,
      errorMessage: hasPlaybackArchive.value
        ? "完整逐行回放暂不可用，当前仅展示精简概要。可重试加载该行详情。"
        : "该行完整回放暂不可用，当前展示已有概要。"
    });
  } finally {
    if (request.isCurrent()) {
      activeRowRequestKey.value = "";
    }
    if (!request.isCurrent()) {
      const state = rowDetailCache.get(cacheKey);
      if (state?.loading) {
        rowDetailCache.delete(cacheKey);
      }
    }
  }
};

const handleRowClick = (row: ExecutionHistorySmartFillRow) => {
  void loadRowDetail(row);
};

const retrySelectedRow = () => {
  if (selectedSummaryRow.value) {
    void loadRowDetail(selectedSummaryRow.value, true);
  }
};

onBeforeUnmount(() => {
  rowRequestGate.cancel();
  activeRowRequestKey.value = "";
});
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
              placeholder="行号/项目/规格/验收/备注/标签"
            />
          </el-form-item>
        </el-form>
      </div>

      <div class="result-table__body">
        <el-table
          :data="pagedRows"
          stripe
          border
          highlight-current-row
          height="100%"
          @row-click="handleRowClick"
        >
          <el-table-column label="行号" width="72">
            <template #default="{ row }">
              {{ row.rowIndex + 1 }}
            </template>
          </el-table-column>
          <el-table-column label="区域/写入列" width="132">
            <template #default="{ row }">
              <span v-if="row.regionIndex !== undefined">
                区域 {{ row.regionIndex + 1 }} · 验收列
                {{ (row.acceptanceColumnIndex ?? 0) + 1 }}
              </span>
              <span v-else>-</span>
            </template>
          </el-table-column>
          <el-table-column
            prop="sourceProject"
            label="项目"
            min-width="140"
            show-overflow-tooltip
          />
          <el-table-column
            prop="sourceSpecification"
            label="规格"
            min-width="180"
            show-overflow-tooltip
          />
          <el-table-column label="匹配来源" width="110">
            <template #default="{ row }">
              {{ getMatchOriginText(row.matchOrigin) }}
            </template>
          </el-table-column>
          <el-table-column
            prop="executionSnapshot.finalAcceptance"
            label="最终验收"
            min-width="180"
            show-overflow-tooltip
          />
          <el-table-column
            prop="executionSnapshot.finalRemark"
            label="最终备注"
            min-width="160"
            show-overflow-tooltip
          />
          <el-table-column label="标签" min-width="220">
            <template #default="{ row }">
              <div class="tag-list">
                <el-tag
                  v-for="tag in row.displayTags"
                  :key="`${row.rowIndex}-${tag}`"
                  size="small"
                  effect="plain"
                >
                  {{ tag }}
                </el-tag>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <el-tag :type="getStatusType(row.status)">
                {{ getStatusText(row.status) }}
              </el-tag>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <ExecutionHistorySmartFillRowDetail
        v-if="selectedSummaryRow"
        class="result-row-detail"
        :row="selectedSummaryRow"
        :detail-row="selectedRowDetailState?.row"
        :loading="selectedRowDetailState?.loading ?? false"
        :error-message="selectedRowDetailState?.errorMessage"
        @retry="retrySelectedRow"
      />

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

.result-row-detail {
  flex: 0 1 42%;
  min-height: 150px;
  max-height: 42%;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}
</style>
