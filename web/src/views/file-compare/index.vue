<script setup lang="ts">
import { ref, computed, watch } from "vue";
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

defineOptions({ name: "FileCompare" });

type CompareViewMode = "document" | "unified";
type WordPaneSide = "left" | "right";

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
let syncing = false;

const isExcel = computed(() => fileType.value === 1);

const totalCount = computed(() => diffItems.value.length);
const wordItems = computed(() => {
  if (!onlyDiff.value) return diffItems.value;
  return diffItems.value.filter((item) => item.diffType !== "Unchanged");
});

const tableOptions = computed(() => {
  const source = tableInfosA.value.length ? tableInfosA.value : tableInfosB.value;
  return source.map((table) => ({
    value: table.index,
    label: isExcel.value
      ? `工作表 ${table.index + 1}${table.name ? `（${table.name}）` : ""}`
      : `表格 ${table.index + 1}`
  }));
});

const currentTableInfoA = computed(
  () => tableInfosA.value.find((item) => item.index === selectedTableIndex.value) ?? null
);

const currentTableInfoB = computed(
  () => tableInfosB.value.find((item) => item.index === selectedTableIndex.value) ?? null
);

const diffMap = computed(() => {
  const map = new Map<string, FileCompareDiffItem>();
  if (selectedTableIndex.value === null) return map;
  diffItems.value.forEach((item) => {
    const tableIndex = item.location?.tableIndex;
    const rowIndex = item.location?.rowIndex;
    const columnIndex = item.location?.columnIndex;
    if (tableIndex === undefined || rowIndex === undefined || columnIndex === undefined) return;
    if (tableIndex !== selectedTableIndex.value) return;
    const key = `${tableIndex}-${rowIndex}-${columnIndex}`;
    map.set(key, item);
  });
  return map;
});

const getExt = (name: string) => name.slice(name.lastIndexOf(".")).toLowerCase();

const resetResult = () => {
  diffItems.value = [];
  diffHunks.value = [];
  addedCount.value = 0;
  removedCount.value = 0;
  modifiedCount.value = 0;
  unchangedCount.value = 0;
  activeView.value = "document";
  onlyDiff.value = false;
  fileType.value = null;
  tableInfosA.value = [];
  tableInfosB.value = [];
  selectedTableIndex.value = null;
  tableDataA.value = null;
  tableDataB.value = null;
};

const handleFileAChange = (uploadFile: UploadFile, _files: UploadFiles) => {
  fileA.value = uploadFile.raw ?? null;
  uploadedA.value = null;
  resetResult();
};

const handleFileBChange = (uploadFile: UploadFile, _files: UploadFiles) => {
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
  if (!validateFiles()) return;

  loading.value = true;
  try {
    const uploadRes = await uploadCompareFiles(fileA.value!, fileB.value!);
    if (uploadRes.code !== 0) {
      ElMessage.error(uploadRes.message || "上传失败");
      return;
    }

    uploadedA.value = uploadRes.data.fileA;
    uploadedB.value = uploadRes.data.fileB;

    const previewRes = await previewCompare({
      fileIdA: uploadRes.data.fileA.fileId,
      fileIdB: uploadRes.data.fileB.fileId
    });
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
    activeView.value = "document";
    onlyDiff.value = false;
    fileType.value = previewRes.data.fileType ?? null;

    await loadTableInfos();

    if (diffItems.value.length === 0) {
      ElMessage.success("未发现差异");
    } else {
      ElMessage.success("对比完成");
    }
  } catch {
    ElMessage.error("对比失败，请重试");
  } finally {
    loading.value = false;
  }
};

const downloadResult = async () => {
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
    a.click();
    window.URL.revokeObjectURL(url);
  } catch {
    ElMessage.error("下载失败");
  }
};

const loadTableInfos = async () => {
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
    getFileTables(uploadedA.value.fileId),
    getFileTables(uploadedB.value.fileId)
  ]);

  tableInfosA.value = resA.code === 0 ? resA.data : [];
  tableInfosB.value = resB.code === 0 ? resB.data : [];

  const firstDiffIndex = diffItems.value.find(
    (item) => item.location?.tableIndex !== undefined
  )?.location?.tableIndex;
  const fallbackIndex =
    tableInfosA.value[0]?.index ?? tableInfosB.value[0]?.index ?? null;

  selectedTableIndex.value = firstDiffIndex ?? fallbackIndex;
};

const loadTablePreviews = async () => {
  if (!uploadedA.value || !uploadedB.value) return;
  if (selectedTableIndex.value === null) return;
  if (!isExcel.value) return;

  const options = {
    previewRows: 0,
    headerRowIndex: 0,
    headerRowCount: 1,
    dataStartRowIndex: 0
  };

  try {
    const [resA, resB] = await Promise.all([
      getTablePreview(uploadedA.value.fileId, selectedTableIndex.value, options),
      getTablePreview(uploadedB.value.fileId, selectedTableIndex.value, options)
    ]);

    tableDataA.value = resA.code === 0 ? resA.data : null;
    tableDataB.value = resB.code === 0 ? resB.data : null;
  } catch {
    ElMessage.error("加载表格预览失败");
  }
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
    return side === "right" ? row.currentText ?? "" : "";
  }
  if (row.diffType === "Removed") {
    return side === "left" ? row.originalText ?? "" : "";
  }
  if (row.diffType === "Modified") {
    return side === "left" ? row.originalText ?? "" : row.currentText ?? "";
  }
  return side === "left"
    ? row.originalText ?? row.currentText ?? ""
    : row.currentText ?? row.originalText ?? "";
};

const getWordPaneDisplayText = (row: FileCompareDiffItem, side: WordPaneSide) => {
  const text = getWordPaneRawText(row, side).trim();
  if (text.length > 0) return text;
  if (row.diffType === "Added" && side === "left") return "（该段为新增，仅存在于文件 B）";
  if (row.diffType === "Removed" && side === "right") return "（该段已删除，仅存在于文件 A）";
  return "（空白段落）";
};

const isWordPanePlaceholder = (row: FileCompareDiffItem, side: WordPaneSide) => {
  return getWordPaneRawText(row, side).trim().length === 0;
};

const getWordParagraphClass = (row: FileCompareDiffItem, side: WordPaneSide) => {
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

watch(
  () => selectedTableIndex.value,
  () => {
    if (isExcel.value) {
      loadTablePreviews();
    }
  }
);

watch(
  () => activeView.value,
  (view) => {
    if (view === "unified" && diffItems.value.length > 0 && !onlyDiff.value) {
      onlyDiff.value = true;
    }
  }
);
</script>

<template>
  <div class="compare-page">
    <el-card class="compare-card">
      <template #header>
        <div class="card-title">文件对比</div>
      </template>

      <el-row :gutter="16">
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
        <el-button type="primary" :loading="loading" @click="startCompare">
          开始对比
        </el-button>
        <el-button :disabled="!diffItems.length" @click="downloadResult">
          下载结果
        </el-button>
      </div>
    </el-card>

    <el-card class="compare-card" v-if="uploadedA && uploadedB">
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

      <div class="table-controls" v-if="activeView === 'document' && tableOptions.length">
        <span class="control-label">
          {{ isExcel ? "选择工作表" : "选择表格" }}
        </span>
        <el-select v-model="selectedTableIndex" placeholder="请选择" class="table-select">
          <el-option
            v-for="item in tableOptions"
            :key="item.value"
            :label="item.label"
            :value="item.value"
          />
        </el-select>
      </div>

      <div class="compare-grid" v-if="activeView === 'document'">
        <div class="compare-pane">
          <div class="pane-title">
            <span>文件 A（原文）</span>
            <span class="pane-file">{{ uploadedA?.fileName }}</span>
          </div>
          <div class="pane-body" ref="leftPaneRef" @scroll="syncScroll('left')">
            <template v-if="isExcel">
              <CompareTableGrid
                :table-index="selectedTableIndex ?? 0"
                :file-type="fileType"
                :table-data="tableDataA"
                :table-info="currentTableInfoA"
                :diff-map="diffMap"
                :only-diff="onlyDiff"
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
                    <span class="paragraph-no">段落 {{ getWordParagraphIndex(row, idx) }}</span>
                    <span class="paragraph-location">{{ buildLocationText(row) }}</span>
                    <el-tag :type="getDiffTagType(row.diffType)" size="small" effect="plain">
                      {{ formatDiffType(row.diffType) }}
                    </el-tag>
                  </header>
                  <p class="paragraph-text" :class="{ 'is-placeholder': isWordPanePlaceholder(row, 'left') }">
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
          <div class="pane-body" ref="rightPaneRef" @scroll="syncScroll('right')">
            <template v-if="isExcel">
              <CompareTableGrid
                :table-index="selectedTableIndex ?? 0"
                :file-type="fileType"
                :table-data="tableDataB"
                :table-info="currentTableInfoB"
                :diff-map="diffMap"
                :only-diff="onlyDiff"
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
                    <span class="paragraph-no">段落 {{ getWordParagraphIndex(row, idx) }}</span>
                    <span class="paragraph-location">{{ buildLocationText(row) }}</span>
                    <el-tag :type="getDiffTagType(row.diffType)" size="small" effect="plain">
                      {{ formatDiffType(row.diffType) }}
                    </el-tag>
                  </header>
                  <p class="paragraph-text" :class="{ 'is-placeholder': isWordPanePlaceholder(row, 'right') }">
                    {{ getWordPaneDisplayText(row, "right") }}
                  </p>
                </article>
              </div>
            </template>
          </div>
        </div>
      </div>

      <UnifiedDiffView v-else :hunks="diffHunks" :items="diffItems" :only-diff="onlyDiff" />
    </el-card>
  </div>
</template>

<style scoped>
.compare-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.compare-card {
  width: 100%;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
}

.upload-text {
  font-size: 14px;
  color: #606266;
}

.actions {
  margin-top: 16px;
  display: flex;
  gap: 12px;
}

.summary {
  display: flex;
  gap: 16px;
  margin-bottom: 12px;
  color: #606266;
  align-items: center;
  flex-wrap: wrap;
}

.diff-toggle {
  margin-left: auto;
}

.view-switch {
  margin-bottom: 12px;
}

.table-controls {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 12px;
}

.control-label {
  font-size: 14px;
  color: #4b5563;
}

.table-select {
  width: 280px;
}

.compare-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.compare-pane {
  border: 1px solid #eef0f3;
  border-radius: 8px;
  overflow: hidden;
  background: #fff;
}

.pane-title {
  padding: 10px 12px;
  font-weight: 600;
  background: #f9fafb;
  border-bottom: 1px solid #eef0f3;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.pane-file {
  font-size: 12px;
  color: #6b7280;
  font-weight: 400;
  max-width: 50%;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pane-body {
  height: min(62vh, 640px);
  overflow: auto;
  background: #fff;
}

.pane-empty {
  padding-top: 72px;
}

.doc-pane {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  background: #f8fafc;
}

.paragraph-card {
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  padding: 10px 12px;
  background: #fff;
}

.paragraph-head {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.paragraph-no {
  font-size: 13px;
  color: #111827;
  font-weight: 600;
}

.paragraph-location {
  font-size: 12px;
  color: #6b7280;
}

.paragraph-text {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.65;
  color: #1f2937;
}

.paragraph-text.is-placeholder {
  color: #94a3b8;
  font-style: italic;
}

.paragraph-card.is-added {
  border-color: #bbf7d0;
  background: #f0fdf4;
}

.paragraph-card.is-removed {
  border-color: #fecaca;
  background: #fef2f2;
}

.paragraph-card.is-modified-old {
  border-color: #fca5a5;
  background: #fff7f7;
}

.paragraph-card.is-modified-new {
  border-color: #86efac;
  background: #f7fff8;
}

.paragraph-card.is-missing {
  border-style: dashed;
  background: #f8fafc;
}

@media (max-width: 1200px) {
  .compare-grid {
    grid-template-columns: 1fr;
  }

  .pane-body {
    height: 520px;
  }
}
</style>
