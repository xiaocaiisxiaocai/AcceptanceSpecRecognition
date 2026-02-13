<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { getTablePreview, type ColumnMapping, type TableData } from "@/api/document";

const props = defineProps<{
  fileId: number;
  tableIndex: number;
  headerRowIndex?: number;
  headerRowCount?: number;
  dataStartRowIndex?: number;
  /** 当前列映射（用于“映射预览”：把原表格映射成 项目/规格/验收/备注 四列） */
  mapping?: ColumnMapping;
}>();

const emit = defineEmits<{
  (e: "loaded", data: TableData): void;
}>();

const loading = ref(false);
const tableData = ref<TableData | null>(null);

const hasAnyMapping = computed(() => {
  const m = props.mapping;
  if (!m) return false;
  return (
    m.projectColumn !== undefined ||
    m.specificationColumn !== undefined ||
    m.acceptanceColumn !== undefined ||
    m.remarkColumn !== undefined
  );
});

const mappedHeaders = ["项目名称", "规格内容", "验收标准", "备注"];

const pick = (row: string[], col?: number) => {
  if (col === undefined || col === null) return "-";
  return row?.[col] ?? "-";
};

const displayHeaders = computed(() => {
  if (!tableData.value) return [];
  return hasAnyMapping.value ? mappedHeaders : tableData.value.headers;
});

const displayRows = computed(() => {
  if (!tableData.value) return [];
  if (!hasAnyMapping.value) return tableData.value.rows;

  const m = props.mapping!;
  return tableData.value.rows.map(r => [
    pick(r, m.projectColumn),
    pick(r, m.specificationColumn),
    pick(r, m.acceptanceColumn),
    pick(r, m.remarkColumn)
  ]);
});

const displayColumnCount = computed(() => {
  if (!tableData.value) return 0;
  return hasAnyMapping.value ? 4 : tableData.value.columnCount;
});

// 加载表格预览
const loadPreview = async () => {
  if (!props.fileId || props.tableIndex === undefined) return;

  loading.value = true;
  try {
    const res = await getTablePreview(props.fileId, props.tableIndex, {
      // 预览全部行（后端约定：previewRows <= 0 表示不限制）
      previewRows: 0,
      headerRowIndex: props.headerRowIndex ?? 0,
      headerRowCount: props.headerRowCount ?? 1,
      dataStartRowIndex: props.dataStartRowIndex ?? 1
    });
    if (res.code === 0) {
      tableData.value = res.data;
      emit("loaded", res.data);
    } else {
      ElMessage.error(res.message || "加载预览失败");
    }
  } catch (error) {
    ElMessage.error("加载预览失败");
  } finally {
    loading.value = false;
  }
};

// 监听参数变化
watch(
  () => [
    props.fileId,
    props.tableIndex,
    props.headerRowIndex,
    props.headerRowCount,
    props.dataStartRowIndex
  ],
  () => {
    loadPreview();
  },
  { immediate: true }
);

// 暴露刷新方法
defineExpose({
  refresh: loadPreview
});
</script>

<template>
  <div class="table-preview">
    <div v-if="loading" class="loading-container">
      <el-skeleton :rows="5" animated />
    </div>

    <div v-else-if="!tableData" class="empty-container">
      <el-empty description="暂无数据" />
    </div>

    <div v-else class="preview-content">
      <div class="preview-info">
        <span>共 {{ tableData.totalRows }} 行数据，{{ displayColumnCount }} 列</span>
        <span v-if="tableData.rows.length < tableData.totalRows" class="preview-tip">
          (显示前 {{ tableData.rows.length }} 行)
        </span>
      </div>

      <div class="table-container">
        <el-table :data="displayRows" border stripe max-height="400" size="small">
          <el-table-column
            v-for="(header, colIndex) in displayHeaders"
            :key="colIndex"
            :label="header || `列${colIndex + 1}`"
            :prop="String(colIndex)"
            min-width="120"
          >
            <template #default="{ row }">
              <span class="cell-content">{{ row[colIndex] || "-" }}</span>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>
  </div>
</template>

<style scoped>
.table-preview {
  width: 100%;
}

.loading-container,
.empty-container {
  padding: 40px 0;
}

.preview-content {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.preview-info {
  font-size: 14px;
  color: #4b5563;
}

.preview-tip {
  color: #94a3b8;
  margin-left: 8px;
}

.table-container {
  width: 100%;
  overflow: auto;
}

.table-preview :deep(.el-table td) {
  vertical-align: top;
}

.cell-content {
  display: block;
  max-width: 320px;
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.5;
}
</style>
