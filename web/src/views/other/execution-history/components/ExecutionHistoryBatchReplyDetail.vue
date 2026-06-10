<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type {
  ExecutionHistoryFile,
  ExecutionHistorySheet
} from "@/api/execution-history";

const props = defineProps<{
  files: ExecutionHistoryFile[];
}>();

const selectedFileIndex = ref(0);
const selectedSheetName = ref("");

const currentFile = computed<ExecutionHistoryFile | null>(
  () => props.files[selectedFileIndex.value] ?? null
);

const currentSheet = computed<ExecutionHistorySheet | null>(() => {
  const sheets = currentFile.value?.sheets ?? [];
  return (
    sheets.find(sheet => sheet.sheetName === selectedSheetName.value) ??
    sheets[0] ??
    null
  );
});

watch(
  () => props.files,
  files => {
    selectedFileIndex.value = 0;
    selectedSheetName.value = files[0]?.sheets[0]?.sheetName ?? "";
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
  <div class="detail-block">
    <div class="section-head">
      <div class="section-title">写回结果</div>
      <div class="section-tip">
        批量回复仅保留简化结果，不展示候选与 AI 复核链路。
      </div>
    </div>

    <div v-if="files.length > 0" class="selector-stack">
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
            max-height="560"
          >
            <el-table-column label="行号" width="80">
              <template #default="{ row }">
                {{ row.rowIndex + 1 }}
              </template>
            </el-table-column>
            <el-table-column
              prop="project"
              label="项目"
              min-width="140"
              show-overflow-tooltip
            />
            <el-table-column
              prop="specification"
              label="规格"
              min-width="180"
              show-overflow-tooltip
            />
            <el-table-column
              prop="acceptance"
              label="验收"
              min-width="180"
              show-overflow-tooltip
            />
            <el-table-column
              prop="remark"
              label="备注"
              min-width="160"
              show-overflow-tooltip
            />
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
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </div>

    <el-empty v-else description="暂无可展示的写回结果" />
  </div>
</template>

<style scoped>
.detail-block {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.section-head {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
}

.section-title {
  font-size: 15px;
  font-weight: 600;
  color: #111827;
}

.section-tip {
  font-size: 12px;
  color: #6b7280;
}

.selector-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.sheet-tabs :deep(.el-tabs__content) {
  padding-top: 8px;
}
</style>
