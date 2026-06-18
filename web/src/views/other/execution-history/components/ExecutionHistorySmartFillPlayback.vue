<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { getExecutionHistorySmartFillRow } from "@/api/execution-history";
import type {
  ExecutionHistoryDetail,
  ExecutionHistorySmartFillFile,
  ExecutionHistorySmartFillRow,
  ExecutionHistorySmartFillSheet
} from "@/api/execution-history";
import type { MatchCandidateOption, MatchPreviewItem } from "@/api/matching";
import ScoreDetailBestMatchSection from "@/views/smart-fill/components/ScoreDetailBestMatchSection.vue";
import ScoreDetailCandidateList from "@/views/smart-fill/components/ScoreDetailCandidateList.vue";
import ScoreDetailDecisionSummarySection from "@/views/smart-fill/components/ScoreDetailDecisionSummarySection.vue";
import { getSelectionModeText } from "@/views/smart-fill/components/scoreDetail.formatters";

const props = defineProps<{
  detail: ExecutionHistoryDetail;
}>();

const selectedFileIndex = ref(0);
const selectedSheetName = ref("");
const selectedRowIndex = ref<number | null>(null);
const comparedCandidateRank = ref(1);
const fullRow = ref<ExecutionHistorySmartFillRow | null>(null);
const fullRowLoading = ref(false);
const fullRowError = ref("");
const fullRowRequestId = ref(0);

const playback = computed(() => props.detail.smartFillPlayback);
const files = computed<ExecutionHistorySmartFillFile[]>(
  () => playback.value?.files ?? []
);
const isSlimmedPlayback = computed(() => playback.value?.isSlimmed === true);

const currentFile = computed<ExecutionHistorySmartFillFile | null>(
  () => files.value[selectedFileIndex.value] ?? null
);

const currentSheet = computed<ExecutionHistorySmartFillSheet | null>(() => {
  const sheets = currentFile.value?.sheets ?? [];
  return (
    sheets.find(sheet => sheet.sheetName === selectedSheetName.value) ??
    sheets[0] ??
    null
  );
});

const currentRow = computed<ExecutionHistorySmartFillRow | null>(() => {
  const rows = currentSheet.value?.rows ?? [];
  if (rows.length === 0) return null;

  return (
    rows.find(row => row.rowIndex === selectedRowIndex.value) ?? rows[0] ?? null
  );
});

const displayRow = computed<ExecutionHistorySmartFillRow | null>(
  () => fullRow.value ?? currentRow.value
);

const currentPreviewItem = computed<MatchPreviewItem | null>(() => {
  if (!displayRow.value) return null;

  return {
    rowIndex: displayRow.value.rowIndex,
    sourceProject: displayRow.value.sourceProject,
    sourceSpecification: displayRow.value.sourceSpecification,
    bestMatch: displayRow.value.previewSnapshot.bestMatch,
    noMatchReason: displayRow.value.previewSnapshot.noMatchReason,
    hasMatch: !!displayRow.value.previewSnapshot.bestMatch,
    confidenceLevel: displayRow.value.previewSnapshot.confidenceLevel
  };
});

const topCandidates = computed<MatchCandidateOption[]>(
  () => currentPreviewItem.value?.bestMatch?.topCandidates ?? []
);

watch(
  files,
  nextFiles => {
    selectedFileIndex.value = 0;
    selectedSheetName.value = nextFiles[0]?.sheets[0]?.sheetName ?? "";
    selectedRowIndex.value = nextFiles[0]?.sheets[0]?.rows[0]?.rowIndex ?? null;
    comparedCandidateRank.value = 1;
    fullRow.value = null;
    fullRowError.value = "";
  },
  { immediate: true }
);

watch(currentFile, file => {
  const firstSheetName = file?.sheets[0]?.sheetName ?? "";
  if (
    file &&
    !file.sheets.some(sheet => sheet.sheetName === selectedSheetName.value)
  ) {
    selectedSheetName.value = firstSheetName;
  }
  fullRow.value = null;
  fullRowError.value = "";
});

watch(currentSheet, sheet => {
  const firstRowIndex = sheet?.rows[0]?.rowIndex ?? null;
  if (
    sheet &&
    !sheet.rows.some(row => row.rowIndex === selectedRowIndex.value)
  ) {
    selectedRowIndex.value = firstRowIndex;
  }
  comparedCandidateRank.value = 1;
  fullRow.value = null;
  fullRowError.value = "";
});

const loadFullRow = async (row: ExecutionHistorySmartFillRow) => {
  if (!playback.value?.hasFullArchive) return;

  const requestId = fullRowRequestId.value + 1;
  fullRowRequestId.value = requestId;
  const sheetIndex = currentFile.value?.sheets.findIndex(
    sheet => sheet.sheetName === currentSheet.value?.sheetName
  );
  if (sheetIndex == null || sheetIndex < 0) return;

  const fileIndex = selectedFileIndex.value;
  const sheetName = currentSheet.value?.sheetName;
  const rowIndex = row.rowIndex;
  fullRowLoading.value = true;
  fullRowError.value = "";
  try {
    const response = await getExecutionHistorySmartFillRow(props.detail.id, {
      fileIndex,
      sheetIndex,
      rowIndex
    });
    if (
      requestId !== fullRowRequestId.value ||
      selectedFileIndex.value !== fileIndex ||
      currentSheet.value?.sheetName !== sheetName ||
      selectedRowIndex.value !== rowIndex
    ) {
      return;
    }
    fullRow.value = response.data;
  } catch (error) {
    if (requestId !== fullRowRequestId.value) return;
    fullRow.value = null;
    fullRowError.value = "完整匹配明细加载失败，当前显示轻量归档信息。";
  } finally {
    if (requestId === fullRowRequestId.value) {
      fullRowLoading.value = false;
    }
  }
};

watch(
  currentRow,
  row => {
    fullRow.value = null;
    fullRowError.value = "";
    if (row) {
      void loadFullRow(row);
    }
  },
  { immediate: true }
);

const handleRowClick = (row: ExecutionHistorySmartFillRow) => {
  selectedRowIndex.value = row.rowIndex;
  comparedCandidateRank.value = 1;
  fullRow.value = null;
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

const isComparedCandidate = (candidate: MatchCandidateOption) =>
  candidate.rank === comparedCandidateRank.value;

const isCandidateExpanded = (candidate: MatchCandidateOption) =>
  candidate.rank === comparedCandidateRank.value;

const handleSelectComparisonCandidate = (candidate: MatchCandidateOption) => {
  comparedCandidateRank.value = candidate.rank;
};

const executionRows = computed(() => {
  if (!displayRow.value) return [];

  return [
    {
      label: "匹配来源",
      value: getMatchOriginText(displayRow.value.matchOrigin)
    },
    {
      label: "选定方式",
      value: getSelectionModeText(
        currentPreviewItem.value?.bestMatch?.selectionMode
      )
    },
    {
      label: "执行状态",
      value: getStatusText(displayRow.value.executionSnapshot.status)
    },
    {
      label: "人工确认",
      value: displayRow.value.executionSnapshot.manualConfirmed ? "是" : "否"
    },
    {
      label: "人工写入",
      value: displayRow.value.executionSnapshot.manualEdited ? "是" : "否"
    },
    {
      label: "最终验收",
      value: displayRow.value.executionSnapshot.finalAcceptance || "-"
    },
    {
      label: "最终备注",
      value: displayRow.value.executionSnapshot.finalRemark || "-"
    },
    {
      label: "验收覆盖值",
      value: displayRow.value.executionSnapshot.overrideAcceptance || "-"
    },
    {
      label: "备注覆盖值",
      value: displayRow.value.executionSnapshot.overrideRemark || "-"
    }
  ];
});
</script>

<template>
  <div class="playback-layout">
    <div class="playback-list">
      <div class="selector-stack">
        <el-segmented
          v-model="selectedFileIndex"
          :options="
            files.map((file, index) => ({
              label: file.fileName,
              value: index
            }))
          "
        />

        <el-tabs v-model="selectedSheetName" class="sheet-tabs">
          <el-tab-pane
            v-for="sheet in currentFile?.sheets ?? []"
            :key="sheet.sheetName"
            :label="sheet.sheetName || `Sheet ${sheet.sheetIndex + 1}`"
            :name="sheet.sheetName"
          >
            <el-table
              :data="currentSheet?.rows ?? []"
              stripe
              border
              highlight-current-row
              row-key="rowIndex"
              max-height="620"
              @row-click="handleRowClick"
            >
              <el-table-column label="行号" width="72">
                <template #default="{ row }">
                  {{ row.rowIndex + 1 }}
                </template>
              </el-table-column>
              <el-table-column
                prop="sourceProject"
                label="项目"
                min-width="120"
                show-overflow-tooltip
              />
              <el-table-column
                prop="sourceSpecification"
                label="规格"
                min-width="180"
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
              <el-table-column label="状态" width="92">
                <template #default="{ row }">
                  <el-tag :type="getStatusType(row.status)">
                    {{ getStatusText(row.status) }}
                  </el-tag>
                </template>
              </el-table-column>
            </el-table>
          </el-tab-pane>
        </el-tabs>
      </div>
    </div>

    <div class="playback-detail">
      <el-alert
        v-if="isSlimmedPlayback"
        class="slimmed-alert"
        type="warning"
        :closable="false"
        title="该执行记录初始加载为轻量视图，展开行详情时会按需读取完整匹配明细。"
      />

      <template v-if="displayRow && currentPreviewItem">
        <el-alert
          v-if="fullRowLoading"
          class="slimmed-alert"
          type="info"
          :closable="false"
          title="正在加载完整匹配明细..."
        />
        <el-alert
          v-if="fullRowError"
          class="slimmed-alert"
          type="warning"
          :closable="false"
          :title="fullRowError"
        />

        <div class="row-head">
          <div>
            <div class="row-head__title">
              第 {{ displayRow.rowIndex + 1 }} 行
            </div>
            <div class="row-head__subtitle">
              {{ displayRow.sourceProject || "-" }} /
              {{ displayRow.sourceSpecification || "-" }}
            </div>
          </div>
          <div class="tag-list">
            <el-tag
              v-for="tag in displayRow.displayTags"
              :key="`active-${displayRow.rowIndex}-${tag}`"
              size="small"
              effect="plain"
            >
              {{ tag }}
            </el-tag>
          </div>
        </div>

        <el-card shadow="never" class="execution-card">
          <template #header>
            <div class="card-title">执行结论</div>
          </template>
          <el-descriptions :column="2" border size="small">
            <el-descriptions-item
              v-for="item in executionRows"
              :key="item.label"
              :label="item.label"
            >
              {{ item.value }}
            </el-descriptions-item>
          </el-descriptions>
        </el-card>

        <el-alert
          v-if="
            !currentPreviewItem.bestMatch && currentPreviewItem.noMatchReason
          "
          type="warning"
          :closable="false"
          :title="currentPreviewItem.noMatchReason"
        />

        <template v-if="currentPreviewItem.bestMatch">
          <ScoreDetailDecisionSummarySection
            :item="currentPreviewItem"
            :source-best-rows="[]"
          />

          <ScoreDetailBestMatchSection :item="currentPreviewItem" />

          <el-card
            v-if="topCandidates.length > 0"
            shadow="never"
            class="candidate-card-wrap"
          >
            <template #header>
              <div class="card-title">候选列表</div>
            </template>
            <ScoreDetailCandidateList
              :top-candidates="topCandidates"
              :is-compared-candidate="isComparedCandidate"
              :is-candidate-expanded="isCandidateExpanded"
              :handle-select-comparison-candidate="
                handleSelectComparisonCandidate
              "
            />
          </el-card>
        </template>
      </template>

      <el-empty v-else description="暂无可回放的行明细" />
    </div>
  </div>
</template>

<style scoped>
.playback-layout {
  display: grid;
  grid-template-columns: minmax(420px, 0.95fr) minmax(520px, 1.15fr);
  gap: 16px;
  align-items: start;
}

.playback-list,
.playback-detail {
  min-width: 0;
}

.selector-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.sheet-tabs :deep(.el-tabs__content) {
  padding-top: 8px;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.row-head {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 12px;
}

.row-head__title {
  font-size: 18px;
  font-weight: 600;
  color: #111827;
}

.row-head__subtitle {
  margin-top: 4px;
  font-size: 13px;
  color: #6b7280;
}

.execution-card,
.candidate-card-wrap {
  margin-bottom: 14px;
}

.slimmed-alert {
  margin-bottom: 12px;
}

.card-title {
  font-size: 14px;
  font-weight: 600;
  color: #111827;
}

@media (width <= 1400px) {
  .playback-layout {
    grid-template-columns: 1fr;
  }
}
</style>
