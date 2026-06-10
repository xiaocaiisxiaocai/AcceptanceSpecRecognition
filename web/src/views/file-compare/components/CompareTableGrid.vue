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

type VisibleRow = {
  row: string[];
  rowIndex: number;
  hasDiff: boolean;
};

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

const rowHasDiff = (rowIndex: number) => {
  for (let columnIndex = 0; columnIndex < columnCount.value; columnIndex += 1) {
    if (getDiffType(rowIndex, columnIndex) !== "Unchanged") {
      return true;
    }
  }
  return false;
};

const visibleRows = computed<VisibleRow[]>(() => {
  const mapped = rows.value.map((row, rowIndex) => ({
    row,
    rowIndex,
    hasDiff: rowHasDiff(rowIndex)
  }));
  if (!props.onlyDiff) return mapped;
  return mapped.filter(item => item.hasDiff);
});

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

const getCellText = (row: string[], columnIndex: number) => {
  const raw = row?.[columnIndex] ?? "";
  return raw.trim().length > 0 ? raw : "（空）";
};

const isEmptyCell = (row: string[], columnIndex: number) => {
  const raw = row?.[columnIndex] ?? "";
  return raw.trim().length === 0;
};
</script>

<template>
  <div class="compare-table" :class="{ 'is-excel': isExcel }">
    <div v-if="!tableData" class="empty-container">
      <el-empty description="暂无数据" />
    </div>

    <div
      v-else-if="rows.length === 0 || columnCount === 0"
      class="empty-container"
    >
      <el-empty description="暂无表格数据" />
    </div>

    <div
      v-else-if="onlyDiff && visibleRows.length === 0"
      class="empty-container"
    >
      <el-empty description="当前工作表无差异" />
    </div>

    <div v-else class="table-scroll">
      <div class="table-toolbar">
        <span>总行数：{{ rows.length }}</span>
        <span v-if="onlyDiff">差异行：{{ visibleRows.length }}</span>
      </div>
      <table class="grid-table">
        <thead v-if="isExcel">
          <tr>
            <th class="corner" />
            <th
              v-for="(col, idx) in columnLabels"
              :key="idx"
              class="col-header"
            >
              {{ col }}
            </th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="item in visibleRows"
            :key="item.rowIndex"
            :class="{ 'row-has-diff': item.hasDiff }"
          >
            <th v-if="isExcel" class="row-header">
              {{ startRow + item.rowIndex }}
            </th>
            <td
              v-for="colIndex in columnCount"
              :key="colIndex"
              :class="getCellClass(item.rowIndex, colIndex - 1)"
            >
              <span
                class="cell-text"
                :class="{
                  'cell-placeholder': isEmptyCell(item.row, colIndex - 1)
                }"
              >
                {{ getCellText(item.row, colIndex - 1) }}
              </span>
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

.table-toolbar {
  position: sticky;
  top: 0;
  z-index: 4;
  display: flex;
  justify-content: space-between;
  padding: 8px 10px;
  font-size: 12px;
  color: #6b7280;
  background: #f8fafc;
  border: 1px solid #e5e7eb;
  border-bottom: none;
}

.table-scroll {
  width: 100%;
  overflow: auto;
}

.grid-table {
  width: 100%;
  min-width: max-content;
  border-collapse: collapse;
  border: 1px solid #e5e7eb;
}

.grid-table th,
.grid-table td {
  padding: 8px 10px;
  font-size: 13px;
  line-height: 1.5;
  vertical-align: top;
  background: #fff;
  border: 1px solid #e5e7eb;
}

.grid-table .corner {
  position: sticky;
  top: 33px;
  left: 0;
  z-index: 3;
  width: 44px;
  background: #f3f4f6;
}

.grid-table .col-header {
  position: sticky;
  top: 33px;
  z-index: 2;
  min-width: 120px;
  text-align: center;
  background: #f3f4f6;
}

.grid-table .row-header {
  position: sticky;
  left: 0;
  z-index: 1;
  min-width: 44px;
  text-align: center;
  background: #f9fafb;
}

.cell-text {
  display: block;
  min-height: 20px;
  word-break: break-word;
  white-space: pre-wrap;
}

.cell-placeholder {
  font-style: italic;
  color: #9ca3af;
}

.cell-added {
  color: #047857;
  background: rgb(16 185 129 / 12%);
}

.cell-removed {
  color: #b91c1c;
  background: rgb(239 68 68 / 12%);
}

.cell-modified {
  color: #92400e;
  background: rgb(245 158 11 / 14%);
}

.cell-unchanged {
  background: #fff;
}

.cell-dim .cell-text {
  color: #9ca3af;
}

.row-has-diff .row-header {
  font-weight: 600;
  color: #1f2937;
}

.empty-container {
  padding: 40px 0;
}
</style>
