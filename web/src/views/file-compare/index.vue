<script setup lang="ts">
import { ref, computed, watch, onBeforeUnmount, onDeactivated } from "vue";
import { ElMessage } from "element-plus";
import type { UploadFile, UploadFiles } from "element-plus";
import {
  uploadCompareFiles,
  previewCompare,
  downloadCompare,
  type FileCompareDiffItem,
  type FileCompareHunk
} from "@/api/file-compare";
import {
  getFileTables,
  getTablePreview,
  type FileUploadResponse,
  type TableInfo,
  type TableData
} from "@/api/document";
import CompareTableGrid from "./components/CompareTableGrid.vue";
import UnifiedDiffView from "./components/UnifiedDiffView.vue";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";
import {
  createLatestRequestGate,
  isCancelledRequest,
  type LatestRequestTicket
} from "./file-compare-request-gate";

defineOptions({ name: "FileComparePage" });

type CompareViewMode = "document" | "unified";
type WordPaneSide = "left" | "right";

const ROW_WINDOW_SIZE = 200;
const COLUMN_WINDOW_SIZE = 60;

const fileA = ref<File | null>(null);
const fileB = ref<File | null>(null);
const uploadedA = ref<FileUploadResponse | null>(null);
const uploadedB = ref<FileUploadResponse | null>(null);
const fileType = ref<number | null>(null);

const loading = ref(false);
const diffItems = ref<FileCompareDiffItem[]>([]);
const diffHunks = ref<FileCompareHunk[]>([]);
const addedCount = ref(0);
const removedCount = ref(0);
const modifiedCount = ref(0);
const unchangedCount = ref(0);

const activeView = ref<CompareViewMode>("document");
const onlyDiff = ref(false);
const tableInfosA = ref<TableInfo[]>([]);
const tableInfosB = ref<TableInfo[]>([]);
const selectedTableIndex = ref<number | null>(null);
const tableDataA = ref<TableData | null>(null);
const tableDataB = ref<TableData | null>(null);
const leftPaneRef = ref<HTMLElement | null>(null);
const rightPaneRef = ref<HTMLElement | null>(null);
const leftTableRef = ref<InstanceType<typeof CompareTableGrid> | null>(null);
const rightTableRef = ref<InstanceType<typeof CompareTableGrid> | null>(null);
const tablePreviewLoading = ref(false);
const rowOffset = ref(0);
const columnOffset = ref(0);
const diffCursor = ref(-1);
const totalItemCount = ref(0);
const workflowRequestGate = createLatestRequestGate();
const tablePreviewRequestGate = createLatestRequestGate();
let syncing = false;

const isExcel = computed(() => fileType.value === 1);
const canUploadCompare = computed(() => hasPerms("btn:file-compare:upload"));
const canPreviewCompare = computed(() => hasPerms("btn:file-compare:preview"));
const canDownloadCompare = computed(() =>
  hasPerms("btn:file-compare:download")
);
const canStartCompare = computed(
  () => canUploadCompare.value && canPreviewCompare.value
);

const totalCount = computed(() => totalItemCount.value);
const wordItems = computed(() => {
  if (!onlyDiff.value) return diffItems.value;
  return diffItems.value.filter(item => item.diffType !== "Unchanged");
});

const tableOptions = computed(() => {
  const source = tableInfosA.value.length
    ? tableInfosA.value
    : tableInfosB.value;
  return source.map(table => ({
    value: table.index,
    label: isExcel.value
      ? `工作表 ${table.index + 1}${table.name ? `（${table.name}）` : ""}`
      : `表格 ${table.index + 1}`
  }));
});

const currentTableInfoA = computed(
  () =>
    tableInfosA.value.find(item => item.index === selectedTableIndex.value) ??
    null
);

const currentTableInfoB = computed(
  () =>
    tableInfosB.value.find(item => item.index === selectedTableIndex.value) ??
    null
);

const totalPreviewRows = computed(() =>
  Math.max(tableDataA.value?.totalRows ?? 0, tableDataB.value?.totalRows ?? 0)
);
const totalPreviewColumns = computed(() =>
  Math.max(
    tableDataA.value?.totalColumns ?? tableDataA.value?.columnCount ?? 0,
    tableDataB.value?.totalColumns ?? tableDataB.value?.columnCount ?? 0
  )
);
const loadedPreviewRows = computed(() =>
  Math.max(
    tableDataA.value?.rows.length ?? 0,
    tableDataB.value?.rows.length ?? 0
  )
);
const loadedPreviewColumns = computed(() =>
  Math.max(
    tableDataA.value?.columnCount ?? 0,
    tableDataB.value?.columnCount ?? 0
  )
);
const rowRangeText = computed(() => {
  if (totalPreviewRows.value === 0 || loadedPreviewRows.value === 0)
    return `行 0 / ${totalPreviewRows.value}`;
  return `行 ${rowOffset.value + 1}-${Math.min(
    rowOffset.value + loadedPreviewRows.value,
    totalPreviewRows.value
  )} / ${totalPreviewRows.value}`;
});
const columnRangeText = computed(() => {
  if (totalPreviewColumns.value === 0 || loadedPreviewColumns.value === 0)
    return `列 0 / ${totalPreviewColumns.value}`;
  return `列 ${columnOffset.value + 1}-${Math.min(
    columnOffset.value + loadedPreviewColumns.value,
    totalPreviewColumns.value
  )} / ${totalPreviewColumns.value}`;
});
const hasPreviousRows = computed(() => rowOffset.value > 0);
const hasNextRows = computed(
  () => rowOffset.value + loadedPreviewRows.value < totalPreviewRows.value
);
const hasPreviousColumns = computed(() => columnOffset.value > 0);
const hasNextColumns = computed(
  () =>
    columnOffset.value + loadedPreviewColumns.value < totalPreviewColumns.value
);
const currentSheetDiffs = computed(() => {
  if (selectedTableIndex.value === null) return [];
  return diffItems.value
    .filter(
      item =>
        item.location?.tableIndex === selectedTableIndex.value &&
        typeof item.location?.rowIndex === "number" &&
        typeof item.location?.columnIndex === "number"
    )
    .sort((a, b) =>
      (a.location.rowIndex ?? 0) === (b.location.rowIndex ?? 0)
        ? (a.location.columnIndex ?? 0) - (b.location.columnIndex ?? 0)
        : (a.location.rowIndex ?? 0) - (b.location.rowIndex ?? 0)
    );
});

const diffMap = computed(() => {
  const map = new Map<string, FileCompareDiffItem>();
  if (selectedTableIndex.value === null) return map;
  diffItems.value.forEach(item => {
    const tableIndex = item.location?.tableIndex;
    const rowIndex = item.location?.rowIndex;
    const columnIndex = item.location?.columnIndex;
    if (
      tableIndex === undefined ||
      rowIndex === undefined ||
      columnIndex === undefined
    )
      return;
    if (tableIndex !== selectedTableIndex.value) return;
    const key = `${tableIndex}-${rowIndex}-${columnIndex}`;
    map.set(key, item);
  });
  return map;
});

const getExt = (name: string) =>
  name.slice(name.lastIndexOf(".")).toLowerCase();

const resetResult = () => {
  diffItems.value = [];
  diffHunks.value = [];
  addedCount.value = 0;
  removedCount.value = 0;
  modifiedCount.value = 0;
  unchangedCount.value = 0;
  totalItemCount.value = 0;
  activeView.value = "document";
  onlyDiff.value = false;
  fileType.value = null;
  tableInfosA.value = [];
  tableInfosB.value = [];
  selectedTableIndex.value = null;
  tableDataA.value = null;
  tableDataB.value = null;
  rowOffset.value = 0;
  columnOffset.value = 0;
  diffCursor.value = -1;
};

const invalidateRequests = () => {
  workflowRequestGate.invalidate();
  tablePreviewRequestGate.invalidate();
  loading.value = false;
  tablePreviewLoading.value = false;
};

const handleFileAChange = (uploadFile: UploadFile, _files: UploadFiles) => {
  invalidateRequests();
  fileA.value = uploadFile.raw ?? null;
  uploadedA.value = null;
  resetResult();
};

const handleFileBChange = (uploadFile: UploadFile, _files: UploadFiles) => {
  invalidateRequests();
  fileB.value = uploadFile.raw ?? null;
  uploadedB.value = null;
  resetResult();
};

const validateFiles = () => {
  if (!fileA.value || !fileB.value) {
    ElMessage.warning("请先选择两份文件");
    return false;
  }

  const extA = getExt(fileA.value.name);
  const extB = getExt(fileB.value.name);
  if (extA !== extB) {
    ElMessage.warning("仅支持同类型文件对比");
    return false;
  }
  if (extA !== ".docx" && extA !== ".xlsx") {
    ElMessage.warning("仅支持 .docx / .xlsx 格式");
    return false;
  }

  return true;
};

const startCompare = async () => {
  if (
    !ensurePermission("btn:file-compare:upload", "权限不足，无法上传对比文件")
  ) {
    return;
  }
  if (
    !ensurePermission("btn:file-compare:preview", "权限不足，无法执行文件对比")
  ) {
    return;
  }
  if (!validateFiles()) return;

  resetResult();
  const sourceFileA = fileA.value!;
  const sourceFileB = fileB.value!;
  const ticket = workflowRequestGate.begin();
  tablePreviewRequestGate.invalidate();
  loading.value = true;
  try {
    const uploadRes = await uploadCompareFiles(sourceFileA, sourceFileB, {
      signal: ticket.controller.signal
    });
    if (
      !workflowRequestGate.isCurrent(ticket) ||
      fileA.value !== sourceFileA ||
      fileB.value !== sourceFileB
    )
      return;
    if (uploadRes.code !== 0) {
      ElMessage.error(uploadRes.message || "上传失败");
      return;
    }

    uploadedA.value = uploadRes.data.fileA;
    uploadedB.value = uploadRes.data.fileB;

    const previewRes = await previewCompare(
      {
        fileIdA: uploadRes.data.fileA.fileId,
        fileIdB: uploadRes.data.fileB.fileId,
        includeUnchanged: false
      },
      {
        signal: ticket.controller.signal
      }
    );
    if (
      !workflowRequestGate.isCurrent(ticket) ||
      uploadedA.value?.fileId !== uploadRes.data.fileA.fileId ||
      uploadedB.value?.fileId !== uploadRes.data.fileB.fileId
    )
      return;
    if (previewRes.code !== 0) {
      ElMessage.error(previewRes.message || "对比失败");
      return;
    }

    diffItems.value = previewRes.data.items || [];
    diffHunks.value = previewRes.data.hunks || [];
    addedCount.value = previewRes.data.addedCount;
    removedCount.value = previewRes.data.removedCount;
    modifiedCount.value = previewRes.data.modifiedCount;
    unchangedCount.value = previewRes.data.unchangedCount ?? 0;
    totalItemCount.value = previewRes.data.totalCount ?? 0;
    activeView.value = "document";
    onlyDiff.value = false;
    fileType.value = previewRes.data.fileType ?? null;

    await loadTableInfos(ticket);

    if (!workflowRequestGate.isCurrent(ticket)) return;

    if (diffItems.value.length === 0) {
      ElMessage.success("未发现差异");
    } else {
      ElMessage.success("对比完成");
    }
  } catch (error) {
    if (workflowRequestGate.isCurrent(ticket) && !isCancelledRequest(error)) {
      ElMessage.error("对比失败，请重试");
    }
  } finally {
    if (workflowRequestGate.isCurrent(ticket)) loading.value = false;
  }
};

const downloadResult = async () => {
  if (
    !ensurePermission("btn:file-compare:download", "权限不足，无法下载对比结果")
  ) {
    return;
  }
  if (!uploadedA.value || !uploadedB.value) {
    ElMessage.warning("请先完成对比");
    return;
  }

  try {
    const blob = await downloadCompare({
      fileIdA: uploadedA.value.fileId,
      fileIdB: uploadedB.value.fileId
    });
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = `compare_${Date.now()}.json`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    window.URL.revokeObjectURL(url);
  } catch {
    ElMessage.error("下载失败");
  }
};

const loadTableInfos = async (ticket: LatestRequestTicket) => {
  if (!uploadedA.value || !uploadedB.value) return;
  if (!isExcel.value) {
    tableInfosA.value = [];
    tableInfosB.value = [];
    selectedTableIndex.value = null;
    tableDataA.value = null;
    tableDataB.value = null;
    return;
  }

  const [resA, resB] = await Promise.all([
    getFileTables(uploadedA.value.fileId, { signal: ticket.controller.signal }),
    getFileTables(uploadedB.value.fileId, { signal: ticket.controller.signal })
  ]);

  if (!workflowRequestGate.isCurrent(ticket)) return;
  if (resA.code !== 0 || resB.code !== 0) {
    throw new Error(resA.message || resB.message || "加载表格列表失败");
  }

  tableInfosA.value = resA.data;
  tableInfosB.value = resB.data;

  const firstDiffIndex = diffItems.value.find(
    item => item.location?.tableIndex !== undefined
  )?.location?.tableIndex;
  const fallbackIndex =
    tableInfosA.value[0]?.index ?? tableInfosB.value[0]?.index ?? null;

  selectedTableIndex.value = firstDiffIndex ?? fallbackIndex;
};

const loadTablePreviews = async () => {
  if (!uploadedA.value || !uploadedB.value) return;
  if (selectedTableIndex.value === null) return;
  if (!isExcel.value) return;

  const currentFileIdA = uploadedA.value.fileId;
  const currentFileIdB = uploadedB.value.fileId;
  const currentTableIndex = selectedTableIndex.value;
  const currentRowOffset = rowOffset.value;
  const currentColumnOffset = columnOffset.value;
  const ticket = tablePreviewRequestGate.begin();
  const options = {
    previewRows: ROW_WINDOW_SIZE,
    headerRowIndex: 0,
    headerRowCount: 1,
    dataStartRowIndex: 0,
    rowOffset: currentRowOffset,
    columnOffset: currentColumnOffset,
    previewColumns: COLUMN_WINDOW_SIZE
  };

  tablePreviewLoading.value = true;
  try {
    const [resA, resB] = await Promise.all([
      getTablePreview(currentFileIdA, currentTableIndex, options, {
        signal: ticket.controller.signal
      }),
      getTablePreview(currentFileIdB, currentTableIndex, options, {
        signal: ticket.controller.signal
      })
    ]);

    if (
      !tablePreviewRequestGate.isCurrent(ticket) ||
      uploadedA.value?.fileId !== currentFileIdA ||
      uploadedB.value?.fileId !== currentFileIdB ||
      selectedTableIndex.value !== currentTableIndex ||
      rowOffset.value !== currentRowOffset ||
      columnOffset.value !== currentColumnOffset
    )
      return;

    if (resA.code !== 0 || resB.code !== 0) {
      throw new Error(resA.message || resB.message || "加载表格预览失败");
    }

    tableDataA.value = resA.data;
    tableDataB.value = resB.data;
  } catch (error) {
    if (
      tablePreviewRequestGate.isCurrent(ticket) &&
      !isCancelledRequest(error)
    ) {
      rowOffset.value =
        tableDataA.value?.rowOffset ?? tableDataB.value?.rowOffset ?? 0;
      columnOffset.value =
        tableDataA.value?.columnOffset ?? tableDataB.value?.columnOffset ?? 0;
      ElMessage.error("加载表格预览失败，请重试");
    }
  } finally {
    if (tablePreviewRequestGate.isCurrent(ticket)) {
      tablePreviewLoading.value = false;
    }
  }
};

const changeRowWindow = (direction: -1 | 1) => {
  rowOffset.value = Math.max(0, rowOffset.value + direction * ROW_WINDOW_SIZE);
  void loadTablePreviews();
};

const changeColumnWindow = (direction: -1 | 1) => {
  columnOffset.value = Math.max(
    0,
    columnOffset.value + direction * COLUMN_WINDOW_SIZE
  );
  void loadTablePreviews();
};

const jumpToDiff = (direction: -1 | 1) => {
  if (currentSheetDiffs.value.length === 0) return;
  diffCursor.value =
    diffCursor.value < 0
      ? direction === 1
        ? 0
        : currentSheetDiffs.value.length - 1
      : (diffCursor.value + direction + currentSheetDiffs.value.length) %
        currentSheetDiffs.value.length;
  const target = currentSheetDiffs.value[diffCursor.value];
  const tableStartRow = Math.min(
    currentTableInfoA.value?.usedRangeStartRow ?? Number.MAX_SAFE_INTEGER,
    currentTableInfoB.value?.usedRangeStartRow ?? Number.MAX_SAFE_INTEGER
  );
  const tableStartColumn = Math.min(
    currentTableInfoA.value?.usedRangeStartColumn ?? Number.MAX_SAFE_INTEGER,
    currentTableInfoB.value?.usedRangeStartColumn ?? Number.MAX_SAFE_INTEGER
  );
  const normalizedStartRow = Number.isFinite(tableStartRow) ? tableStartRow : 1;
  const normalizedStartColumn = Number.isFinite(tableStartColumn)
    ? tableStartColumn
    : 1;
  const relativeRow = Math.max(
    0,
    (target.location.rowIndex ?? normalizedStartRow) - normalizedStartRow
  );
  const relativeColumn = Math.max(
    0,
    (target.location.columnIndex ?? normalizedStartColumn) -
      normalizedStartColumn
  );
  rowOffset.value = Math.floor(relativeRow / ROW_WINDOW_SIZE) * ROW_WINDOW_SIZE;
  columnOffset.value =
    Math.floor(relativeColumn / COLUMN_WINDOW_SIZE) * COLUMN_WINDOW_SIZE;
  void loadTablePreviews();
};

const formatDiffType = (type: FileCompareDiffItem["diffType"]) => {
  if (type === "Unchanged") return "一致";
  if (type === "Added") return "新增";
  if (type === "Removed") return "删除";
  return "修改";
};

const buildLocationText = (row: FileCompareDiffItem) => {
  if (row.displayLocation) return row.displayLocation;
  const loc = row.location || ({} as FileCompareDiffItem["location"]);
  if (loc.rowIndex !== undefined) return `段落${(loc.rowIndex ?? 0) + 1}`;
  return "未定位";
};

const getDiffTagType = (type: FileCompareDiffItem["diffType"]) => {
  if (type === "Unchanged") return "info";
  if (type === "Added") return "success";
  if (type === "Removed") return "danger";
  return "warning";
};

const getWordParagraphIndex = (row: FileCompareDiffItem, idx: number) => {
  const rowIndex = row.location?.rowIndex;
  if (typeof rowIndex === "number") return rowIndex + 1;
  return idx + 1;
};

const getWordPaneRawText = (row: FileCompareDiffItem, side: WordPaneSide) => {
  if (row.diffType === "Added") {
    return side === "right" ? (row.currentText ?? "") : "";
  }
  if (row.diffType === "Removed") {
    return side === "left" ? (row.originalText ?? "") : "";
  }
  if (row.diffType === "Modified") {
    return side === "left" ? (row.originalText ?? "") : (row.currentText ?? "");
  }
  return side === "left"
    ? (row.originalText ?? row.currentText ?? "")
    : (row.currentText ?? row.originalText ?? "");
};

const getWordPaneDisplayText = (
  row: FileCompareDiffItem,
  side: WordPaneSide
) => {
  const text = getWordPaneRawText(row, side).trim();
  if (text.length > 0) return text;
  if (row.diffType === "Added" && side === "left")
    return "（该段为新增，仅存在于文件 B）";
  if (row.diffType === "Removed" && side === "right")
    return "（该段已删除，仅存在于文件 A）";
  return "（空白段落）";
};

const isWordPanePlaceholder = (
  row: FileCompareDiffItem,
  side: WordPaneSide
) => {
  return getWordPaneRawText(row, side).trim().length === 0;
};

const getWordParagraphClass = (
  row: FileCompareDiffItem,
  side: WordPaneSide
) => {
  return {
    "paragraph-card": true,
    "is-unchanged": row.diffType === "Unchanged",
    "is-modified-old": row.diffType === "Modified" && side === "left",
    "is-modified-new": row.diffType === "Modified" && side === "right",
    "is-added": row.diffType === "Added" && side === "right",
    "is-removed": row.diffType === "Removed" && side === "left",
    "is-missing":
      (row.diffType === "Added" && side === "left") ||
      (row.diffType === "Removed" && side === "right")
  };
};

const syncScroll = (source: "left" | "right") => {
  if (syncing) return;
  const left = leftPaneRef.value;
  const right = rightPaneRef.value;
  if (!left || !right) return;
  syncing = true;
  if (source === "left") {
    right.scrollTop = left.scrollTop;
    right.scrollLeft = left.scrollLeft;
  } else {
    left.scrollTop = right.scrollTop;
    left.scrollLeft = right.scrollLeft;
  }
  requestAnimationFrame(() => {
    syncing = false;
  });
};

const syncTableScroll = (
  source: "left" | "right",
  payload: { scrollLeft: number }
) => {
  if (syncing || !isExcel.value) return;
  syncing = true;

  const targetTable =
    source === "left" ? rightTableRef.value : leftTableRef.value;
  targetTable?.setScrollLeft(payload.scrollLeft);

  requestAnimationFrame(() => {
    syncing = false;
  });
};

watch(
  () => selectedTableIndex.value,
  () => {
    tablePreviewRequestGate.invalidate();
    rowOffset.value = 0;
    columnOffset.value = 0;
    diffCursor.value = -1;
    tableDataA.value = null;
    tableDataB.value = null;
    if (isExcel.value) {
      void loadTablePreviews();
    }
  }
);

watch(
  () => activeView.value,
  view => {
    if (view === "unified" && diffItems.value.length > 0 && !onlyDiff.value) {
      onlyDiff.value = true;
    }
  }
);

onDeactivated(invalidateRequests);
onBeforeUnmount(invalidateRequests);
</script>

<template>
  <div class="page page--fill file-compare-page">
    <el-card class="compare-card">
      <template #header>
        <div class="card-title">文件对比</div>
      </template>

      <el-alert
        v-if="!canUploadCompare"
        type="warning"
        :closable="false"
        show-icon
        title="当前账号没有文件对比上传权限"
      />
      <el-row v-else :gutter="16">
        <el-col :span="12">
          <el-upload
            drag
            :show-file-list="true"
            :auto-upload="false"
            :limit="1"
            accept=".docx,.xlsx"
            :on-change="handleFileAChange"
          >
            <div class="upload-text">选择文件 A</div>
          </el-upload>
        </el-col>
        <el-col :span="12">
          <el-upload
            drag
            :show-file-list="true"
            :auto-upload="false"
            :limit="1"
            accept=".docx,.xlsx"
            :on-change="handleFileBChange"
          >
            <div class="upload-text">选择文件 B</div>
          </el-upload>
        </el-col>
      </el-row>

      <div class="actions">
        <el-button
          v-if="canStartCompare"
          type="primary"
          :loading="loading"
          @click="startCompare"
        >
          开始对比
        </el-button>
        <el-button
          v-if="canDownloadCompare"
          :disabled="!diffItems.length"
          @click="downloadResult"
        >
          下载结果
        </el-button>
      </div>
    </el-card>

    <el-card v-if="uploadedA && uploadedB" class="compare-card">
      <template #header>
        <div class="card-title">对比结果</div>
      </template>

      <div class="summary">
        <span>新增：{{ addedCount }}</span>
        <span>删除：{{ removedCount }}</span>
        <span>修改：{{ modifiedCount }}</span>
        <span>一致：{{ unchangedCount }}</span>
        <span>总计：{{ totalCount }}</span>
        <el-switch
          v-model="onlyDiff"
          active-text="仅显示差异"
          inactive-text="显示全部"
          class="diff-toggle"
        />
      </div>

      <div class="view-switch">
        <el-radio-group v-model="activeView" size="small">
          <el-radio-button label="document">原文档视图</el-radio-button>
          <el-radio-button label="unified">差异视图</el-radio-button>
        </el-radio-group>
      </div>

      <div
        v-if="activeView === 'document' && tableOptions.length"
        class="table-controls"
      >
        <div class="table-selector">
          <span class="control-label">
            {{ isExcel ? "选择工作表" : "选择表格" }}
          </span>
          <el-select
            v-model="selectedTableIndex"
            placeholder="请选择"
            class="table-select"
          >
            <el-option
              v-for="item in tableOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </div>
        <div
          v-if="isExcel"
          class="window-controls"
          role="group"
          aria-label="表格预览范围"
        >
          <span class="window-range">{{ rowRangeText }}</span>
          <span class="window-range">{{ columnRangeText }}</span>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || !hasPreviousRows"
            @click="changeRowWindow(-1)"
          >
            上一批行
          </el-button>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || !hasNextRows"
            @click="changeRowWindow(1)"
          >
            下一批行
          </el-button>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || !hasPreviousColumns"
            @click="changeColumnWindow(-1)"
          >
            左移列
          </el-button>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || !hasNextColumns"
            @click="changeColumnWindow(1)"
          >
            右移列
          </el-button>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || currentSheetDiffs.length === 0"
            @click="jumpToDiff(-1)"
          >
            上一处差异
          </el-button>
          <el-button
            size="small"
            :disabled="tablePreviewLoading || currentSheetDiffs.length === 0"
            @click="jumpToDiff(1)"
          >
            下一处差异
          </el-button>
        </div>
      </div>

      <div
        v-if="activeView === 'document'"
        v-loading="tablePreviewLoading"
        class="compare-grid"
      >
        <div class="compare-pane">
          <div class="pane-title">
            <span>文件 A（原文）</span>
            <span class="pane-file">{{ uploadedA?.fileName }}</span>
          </div>
          <div ref="leftPaneRef" class="pane-body" @scroll="syncScroll('left')">
            <template v-if="isExcel">
              <CompareTableGrid
                ref="leftTableRef"
                :table-index="selectedTableIndex ?? 0"
                :file-type="fileType"
                :table-data="tableDataA"
                :table-info="currentTableInfoA"
                :diff-map="diffMap"
                :only-diff="onlyDiff"
                @scroll="syncTableScroll('left', $event)"
              />
            </template>
            <template v-else>
              <div v-if="!wordItems.length" class="pane-empty">
                <el-empty description="暂无文本内容" :image-size="80" />
              </div>
              <div v-else class="doc-pane">
                <article
                  v-for="(row, idx) in wordItems"
                  :key="`${row.displayLocation || idx}-old`"
                  :class="getWordParagraphClass(row, 'left')"
                >
                  <header class="paragraph-head">
                    <span class="paragraph-no"
                      >段落 {{ getWordParagraphIndex(row, idx) }}</span
                    >
                    <span class="paragraph-location">{{
                      buildLocationText(row)
                    }}</span>
                    <el-tag
                      :type="getDiffTagType(row.diffType)"
                      size="small"
                      effect="plain"
                    >
                      {{ formatDiffType(row.diffType) }}
                    </el-tag>
                  </header>
                  <p
                    class="paragraph-text"
                    :class="{
                      'is-placeholder': isWordPanePlaceholder(row, 'left')
                    }"
                  >
                    {{ getWordPaneDisplayText(row, "left") }}
                  </p>
                </article>
              </div>
            </template>
          </div>
        </div>
        <div class="compare-pane">
          <div class="pane-title">
            <span>文件 B（新文）</span>
            <span class="pane-file">{{ uploadedB?.fileName }}</span>
          </div>
          <div
            ref="rightPaneRef"
            class="pane-body"
            @scroll="syncScroll('right')"
          >
            <template v-if="isExcel">
              <CompareTableGrid
                ref="rightTableRef"
                :table-index="selectedTableIndex ?? 0"
                :file-type="fileType"
                :table-data="tableDataB"
                :table-info="currentTableInfoB"
                :diff-map="diffMap"
                :only-diff="onlyDiff"
                @scroll="syncTableScroll('right', $event)"
              />
            </template>
            <template v-else>
              <div v-if="!wordItems.length" class="pane-empty">
                <el-empty description="暂无文本内容" :image-size="80" />
              </div>
              <div v-else class="doc-pane">
                <article
                  v-for="(row, idx) in wordItems"
                  :key="`${row.displayLocation || idx}-new`"
                  :class="getWordParagraphClass(row, 'right')"
                >
                  <header class="paragraph-head">
                    <span class="paragraph-no"
                      >段落 {{ getWordParagraphIndex(row, idx) }}</span
                    >
                    <span class="paragraph-location">{{
                      buildLocationText(row)
                    }}</span>
                    <el-tag
                      :type="getDiffTagType(row.diffType)"
                      size="small"
                      effect="plain"
                    >
                      {{ formatDiffType(row.diffType) }}
                    </el-tag>
                  </header>
                  <p
                    class="paragraph-text"
                    :class="{
                      'is-placeholder': isWordPanePlaceholder(row, 'right')
                    }"
                  >
                    {{ getWordPaneDisplayText(row, "right") }}
                  </p>
                </article>
              </div>
            </template>
          </div>
        </div>
      </div>

      <UnifiedDiffView
        v-else
        :hunks="diffHunks"
        :items="diffItems"
        :only-diff="onlyDiff"
      />
    </el-card>
  </div>
</template>

<style scoped>
.file-compare-page {
  display: flex;
  flex-direction: column;
  min-height: 0;
}

.compare-card {
  width: 100%;
}

.compare-card:last-child {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.compare-card:last-child :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
}

.upload-text {
  font-size: 14px;
  color: var(--app-text-secondary);
}

.actions {
  display: flex;
  gap: 12px;
  margin-top: 16px;
}

.summary {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  align-items: center;
  margin-bottom: 12px;
  color: var(--app-text-secondary);
}

.diff-toggle {
  margin-left: auto;
}

.view-switch {
  margin-bottom: 12px;
}

.table-controls {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 12px;
}

.table-selector,
.window-controls {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
}

.window-range {
  font-size: 12px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.control-label {
  font-size: 14px;
  color: var(--app-text-secondary);
}

.table-select {
  width: 280px;
}

.compare-grid {
  display: grid;
  flex: 1;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  min-height: 0;
}

.compare-pane {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: hidden;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
}

.pane-title {
  display: flex;
  gap: 12px;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  font-weight: 600;
  background: var(--app-info-bg);
  border-bottom: 1px solid var(--app-border);
}

.pane-file {
  max-width: 50%;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 12px;
  font-weight: 400;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.pane-body {
  flex: 1;
  height: 100%;
  min-height: 0;
  overflow: auto;
  background: var(--app-bg-card);
}

.pane-empty {
  padding-top: 72px;
}

.doc-pane {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  background: var(--app-info-bg);
}

.paragraph-card {
  padding: 10px 12px;
  background: var(--app-bg-card);
  border: 1px solid var(--app-border);
  border-radius: var(--app-radius-sm);
}

.paragraph-head {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-items: center;
  margin-bottom: 6px;
}

.paragraph-no {
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.paragraph-location {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.paragraph-text {
  margin: 0;
  line-height: 1.65;
  color: var(--app-text-primary);
  word-break: break-word;
  white-space: pre-wrap;
}

.paragraph-text.is-placeholder {
  font-style: italic;
  color: var(--app-text-disabled);
}

.paragraph-card.is-added {
  background: var(--app-diff-add-bg);
  border-color: var(--app-diff-add-emphasis);
}

.paragraph-card.is-removed {
  background: var(--app-diff-del-bg);
  border-color: var(--app-diff-del-emphasis);
}

.paragraph-card.is-modified-old {
  background: var(--app-diff-del-bg);
  border-color: var(--app-diff-del-emphasis);
}

.paragraph-card.is-modified-new {
  background: var(--app-diff-add-bg);
  border-color: var(--app-diff-add-emphasis);
}

.paragraph-card.is-missing {
  background: var(--app-info-bg);
  border-style: dashed;
}

@media (width <= 1200px) {
  .compare-grid {
    grid-template-columns: 1fr;
  }
}
</style>
