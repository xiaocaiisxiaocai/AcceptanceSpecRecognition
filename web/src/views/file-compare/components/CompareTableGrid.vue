<script setup lang="ts">
import { computed } from "vue";
import type { TableData, TableInfo } from "@/api/document";
import type { FileCompareDiffItem } from "@/api/file-compare";

const props = defineProps<{
  tableIndex: number;
  fileType: number | null;
  tableData: TableData | null;
  tableInfo?: TableInfo | null;
  diffMap: Map<string, FileCompareDiffItem>;
  onlyDiff: boolean;
}>();

const isExcel = computed(() => props.fileType === 1);

const startRow = computed(() => props.tableInfo?.usedRangeStartRow ?? 1);
const startCol = computed(() => props.tableInfo?.usedRangeStartColumn ?? 1);

const columnCount = computed(() => props.tableData?.columnCount ?? 0);
const rows = computed(() => props.tableData?.rows ?? []);

const toExcelColumnName = (columnNumber: number) => {
  let dividend = columnNumber;
  let columnName = "";
  while (dividend > 0) {
    const modulo = (dividend - 1) % 26;
    columnName = String.fromCharCode("A".charCodeAt(0) + modulo) + columnName;
    dividend = Math.floor((dividend - modulo) / 26);
  }
  return columnName || "";
};

const columnLabels = computed(() => {
  if (!isExcel.value) return [] as string[];
  return Array.from({ length: columnCount.value }, (_, idx) =>
    toExcelColumnName(startCol.value + idx)
  );
});

const getDiffType = (rowIndex: number, columnIndex: number) => {
  const absRow = isExcel.value ? startRow.value + rowIndex : rowIndex;
  const absCol = isExcel.value ? startCol.value + columnIndex : columnIndex;
  const key = `${props.tableIndex}-${absRow}-${absCol}`;
  return props.diffMap.get(key)?.diffType ?? "Unchanged";
};

const getCellClass = (rowIndex: number, columnIndex: number) => {
  const diffType = getDiffType(rowIndex, columnIndex);
  return {
    "cell-unchanged": diffType === "Unchanged",
    "cell-added": diffType === "Added",
    "cell-removed": diffType === "Removed",
    "cell-modified": diffType === "Modified",
    "cell-dim": props.onlyDiff && diffType === "Unchanged"
  };
};

const getCellText = (row: string[], columnIndex: number, rowIndex: number) => {
  const raw = row?.[columnIndex] ?? "";
  if (props.onlyDiff) {
    const diffType = getDiffType(rowIndex, columnIndex);
    if (diffType === "Unchanged") return "";
  }
  return raw;
};
</script>

<template>
  <div class="compare-table" :class="{ 'is-excel': isExcel }">
    <div v-if="!tableData" class="empty-container">
      <el-empty description="暂无数据" />
    </div>

    <div v-else-if="rows.length === 0 || columnCount === 0" class="empty-container">
      <el-empty description="暂无表格数据" />
    </div>

    <div v-else class="table-scroll">
      <table class="grid-table">
        <thead v-if="isExcel">
          <tr>
            <th class="corner"></th>
            <th v-for="(col, idx) in columnLabels" :key="idx" class="col-header">
              {{ col }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, rowIndex) in rows" :key="rowIndex">
            <th v-if="isExcel" class="row-header">
              {{ startRow + rowIndex }}
            </th>
            <td
              v-for="colIndex in columnCount"
              :key="colIndex"
              :class="getCellClass(rowIndex, colIndex - 1)"
            >
              <span class="cell-text">{{ getCellText(row, colIndex - 1, rowIndex) }}</span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.compare-table {
  width: 100%;
}

.table-scroll {
  width: 100%;
  overflow: auto;
}

.grid-table {
  border-collapse: collapse;
  width: 100%;
  min-width: max-content;
}

.grid-table th,
.grid-table td {
  border: 1px solid #e5e7eb;
  padding: 6px 8px;
  font-size: 13px;
  line-height: 1.4;
  vertical-align: top;
  background: #fff;
}

.grid-table .corner {
  position: sticky;
  left: 0;
  top: 0;
  z-index: 3;
  background: #f3f4f6;
  width: 44px;
}

.grid-table .col-header {
  position: sticky;
  top: 0;
  z-index: 2;
  background: #f3f4f6;
  text-align: center;
  min-width: 120px;
}

.grid-table .row-header {
  position: sticky;
  left: 0;
  z-index: 1;
  background: #f9fafb;
  text-align: center;
  min-width: 44px;
}

.cell-text {
  display: block;
  white-space: pre-wrap;
  word-break: break-word;
  min-height: 18px;
}

.cell-added {
  background: rgba(16, 185, 129, 0.12);
  color: #047857;
}

.cell-removed {
  background: rgba(239, 68, 68, 0.12);
  color: #b91c1c;
}

.cell-modified {
  background: rgba(245, 158, 11, 0.14);
  color: #92400e;
}

.cell-unchanged {
  background: #fff;
}

.cell-dim .cell-text {
  color: #cbd5e1;
}

.empty-container {
  padding: 40px 0;
}
</style>
