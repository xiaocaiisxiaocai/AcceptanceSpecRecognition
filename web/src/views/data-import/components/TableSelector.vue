<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { getFileTables, type TableInfo } from "@/api/document";

const props = defineProps<{
  fileId: number;
  /** 是否允许多选表格（默认单选） */
  multiple?: boolean;
  modelValue?: number | number[];
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: number | number[]): void;
  (e: "selected", value: TableInfo): void;
  (e: "selectedMultiple", value: TableInfo[]): void;
}>();

const loading = ref(false);
const tables = ref<TableInfo[]>([]);
const selectedIndex = ref<number | null>(null);
const selectedIndexes = ref<number[]>([]);

const allSelected = computed(() => {
  if (!props.multiple) return false;
  if (tables.value.length === 0) return false;
  return selectedIndexes.value.length === tables.value.length;
});

const toggleSelectAll = (val: boolean) => {
  if (!props.multiple) return;
  if (val) {
    selectedIndexes.value = tables.value.map(t => t.index).sort((a, b) => a - b);
  } else {
    selectedIndexes.value = [];
  }
  emit("update:modelValue", [...selectedIndexes.value]);
  emit(
    "selectedMultiple",
    tables.value.filter(t => selectedIndexes.value.includes(t.index))
  );
};

// 加载表格列表
const loadTables = async () => {
  if (!props.fileId) return;

  loading.value = true;
  try {
    const res = await getFileTables(props.fileId);
    if (res.code === 0) {
      tables.value = res.data;
      // 如果有modelValue，选中对应表格
      if (props.modelValue !== undefined) {
        if (props.multiple) {
          selectedIndexes.value = Array.isArray(props.modelValue)
            ? props.modelValue
            : [props.modelValue];
        } else {
          selectedIndex.value = Array.isArray(props.modelValue)
            ? (props.modelValue[0] ?? null)
            : props.modelValue;
        }
      }
    } else {
      ElMessage.error(res.message || "加载表格列表失败");
    }
  } catch (error) {
    ElMessage.error("加载表格列表失败");
  } finally {
    loading.value = false;
  }
};

// 选择表格
const handleSelect = (table: TableInfo) => {
  if (props.multiple) {
    const idx = selectedIndexes.value.indexOf(table.index);
    if (idx >= 0) selectedIndexes.value.splice(idx, 1);
    else selectedIndexes.value.push(table.index);
    selectedIndexes.value.sort((a, b) => a - b);
    emit("update:modelValue", [...selectedIndexes.value]);
    emit(
      "selectedMultiple",
      tables.value.filter(t => selectedIndexes.value.includes(t.index))
    );
  } else {
    selectedIndex.value = table.index;
    emit("update:modelValue", table.index);
    emit("selected", table);
  }
};

// 监听fileId变化
watch(
  () => props.fileId,
  () => {
    loadTables();
  },
  { immediate: true }
);

// 格式化预览文本
const formatPreview = (text?: string) => {
  if (!text) return "无预览";
  return text.length > 100 ? text.substring(0, 100) + "..." : text;
};
</script>

<template>
  <div class="table-selector">
    <div v-if="loading" class="loading-container">
      <el-skeleton :rows="3" animated />
    </div>

    <div v-else-if="tables.length === 0" class="empty-container">
      <el-empty description="该文件中没有表格" />
    </div>

    <div v-else class="table-list">
      <div v-if="props.multiple" class="bulk-actions">
        <el-checkbox
          :model-value="allSelected"
          @change="(v: any) => toggleSelectAll(!!v)"
        >
          全选
        </el-checkbox>
        <span class="bulk-tip">
          已选 {{ selectedIndexes.length }} / {{ tables.length }}
        </span>
        <el-button size="small" @click.stop="toggleSelectAll(true)">全选</el-button>
        <el-button size="small" @click.stop="toggleSelectAll(false)">清空</el-button>
      </div>

      <div
        v-for="table in tables"
        :key="table.index"
        class="table-item"
        :class="{
          selected: props.multiple
            ? selectedIndexes.includes(table.index)
            : selectedIndex === table.index
        }"
        @click="handleSelect(table)"
      >
        <div class="table-header">
          <div class="table-title">
            <span class="table-index">表格 {{ table.index + 1 }}</span>
            <span class="table-meta">（{{ table.rowCount }} 行 × {{ table.columnCount }} 列）</span>
          </div>
          <div class="table-tags">
            <el-tag v-if="table.isNested" type="warning" size="small">
              嵌套表格
            </el-tag>
            <el-tag v-if="table.hasMergedCells" type="info" size="small">
              有合并单元格
            </el-tag>
          </div>
        </div>
        <div v-if="table.headers.length > 0" class="table-headers">
          <span class="label">表头：</span>
          <span class="headers">{{ table.headers.join(" | ") }}</span>
        </div>
        <div
          v-if="
            props.multiple
              ? selectedIndexes.includes(table.index)
              : selectedIndex === table.index
          "
          class="selected-indicator"
        >
          <el-icon color="#409EFF">
            <svg viewBox="0 0 24 24" fill="currentColor">
              <path d="M9 16.17L4.83 12l-1.42 1.41L9 19 21 7l-1.41-1.41z" />
            </svg>
          </el-icon>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.table-selector {
  width: 100%;
}

.loading-container,
.empty-container {
  padding: 40px 0;
}

.table-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border: 1px solid #ebeef5;
  border-radius: 8px;
  background: #fafafa;
}

.bulk-tip {
  font-size: 12px;
  color: #909399;
  margin-right: auto;
}

.table-item {
  position: relative;
  padding: 16px;
  border: 2px solid #e4e7ed;
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;
}

.table-item:hover {
  border-color: #c0c4cc;
  background-color: #f5f7fa;
}

.table-item.selected {
  border-color: #409eff;
  background-color: #ecf5ff;
}

.table-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.table-index {
  font-size: 16px;
  font-weight: 600;
  color: #303133;
}

.table-title {
  display: flex;
  align-items: baseline;
  gap: 8px;
  min-width: 0;
}

.table-meta {
  font-size: 13px;
  color: #909399;
  white-space: nowrap;
}

.table-tags {
  display: flex;
  gap: 8px;
}

.table-headers {
  font-size: 13px;
  color: #909399;
  margin-bottom: 8px;
  display: flex;
  align-items: flex-start;
}

.table-headers .label {
  flex-shrink: 0;
  margin-right: 4px;
}

.table-headers .headers {
  word-break: break-all;
}

.selected-indicator {
  position: absolute;
  top: 16px;
  right: 16px;
}
</style>
