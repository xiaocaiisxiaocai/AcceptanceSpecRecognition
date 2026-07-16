<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage } from "element-plus";
import { getFileTables, type TableInfo } from "@/api/document";
import { getRequestErrorMessage } from "@/utils/error-message";

const props = defineProps<{
  fileId: number;
  /** 是否允许多选表格（默认单选） */
  multiple?: boolean;
  modelValue?: number | number[];
  itemLabel?: string;
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
const searchKeyword = ref("");
const compactMode = ref(false);

const syncLocalSelectionFromModel = () => {
  if (props.multiple) {
    selectedIndexes.value = Array.isArray(props.modelValue)
      ? [...props.modelValue]
      : props.modelValue !== undefined && props.modelValue !== null
        ? [props.modelValue]
        : [];
  } else {
    selectedIndex.value = Array.isArray(props.modelValue)
      ? (props.modelValue[0] ?? null)
      : (props.modelValue ?? null);
  }
};

const emitMultipleSelection = () => {
  emit("update:modelValue", [...selectedIndexes.value]);
  emit(
    "selectedMultiple",
    tables.value.filter(t => selectedIndexes.value.includes(t.index))
  );
};

const filteredTables = computed(() => {
  const keyword = searchKeyword.value.trim().toLowerCase();
  if (!keyword) return tables.value;

  return tables.value.filter(table => {
    const haystack = [
      String(table.index + 1),
      table.name ?? "",
      `${table.rowCount}x${table.columnCount}`,
      ...(table.headers ?? [])
    ]
      .join(" ")
      .toLowerCase();
    return haystack.includes(keyword);
  });
});

const filteredSelectedCount = computed(
  () =>
    filteredTables.value.filter(table =>
      selectedIndexes.value.includes(table.index)
    ).length
);

const allSelected = computed(() => {
  if (!props.multiple) return false;
  if (filteredTables.value.length === 0) return false;
  return filteredSelectedCount.value === filteredTables.value.length;
});

const itemLabel = computed(() => props.itemLabel || "表格");

const toggleSelectAll = (val: boolean) => {
  if (!props.multiple) return;
  const filteredIndexes = filteredTables.value.map(t => t.index);
  if (val) {
    selectedIndexes.value = Array.from(
      new Set([...selectedIndexes.value, ...filteredIndexes])
    ).sort((a, b) => a - b);
  } else {
    const filteredSet = new Set(filteredIndexes);
    selectedIndexes.value = selectedIndexes.value.filter(
      index => !filteredSet.has(index)
    );
  }
  emitMultipleSelection();
};

// 加载表格列表
const loadTables = async () => {
  if (!props.fileId) return;

  loading.value = true;
  try {
    const res = await getFileTables(props.fileId);
    if (res.code === 0) {
      tables.value = res.data;
      // 根据外部 modelValue 同步本地选中态
      syncLocalSelectionFromModel();
    } else {
      ElMessage.error(res.message || "加载表格列表失败");
    }
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "加载表格列表失败"));
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
    emitMultipleSelection();
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

watch(
  () => props.modelValue,
  () => {
    syncLocalSelectionFromModel();
  }
);
</script>

<template>
  <div class="table-selector">
    <div v-if="loading" class="loading-container">
      <el-skeleton :rows="3" animated />
    </div>

    <div v-else-if="tables.length === 0" class="empty-container">
      <el-empty :description="`该文件中没有${itemLabel}`" />
    </div>

    <div v-else class="table-list" :class="{ compact: compactMode }">
      <div class="table-list-actions">
        <el-input
          v-model="searchKeyword"
          class="table-search"
          clearable
          size="small"
          :placeholder="`搜索${itemLabel}名称或表头`"
        />
        <el-switch
          v-model="compactMode"
          size="small"
          active-text="紧凑"
          inactive-text="标准"
        />
      </div>

      <div v-if="props.multiple" class="bulk-actions">
        <el-checkbox
          :model-value="allSelected"
          :indeterminate="filteredSelectedCount > 0 && !allSelected"
          @change="
            (v: string | number | boolean) => toggleSelectAll(Boolean(v))
          "
        >
          全选当前列表
        </el-checkbox>
        <span class="bulk-tip">
          已选 {{ selectedIndexes.length }} / {{ tables.length }}，当前显示
          {{ filteredTables.length }}
        </span>
        <el-button size="small" @click.stop="toggleSelectAll(true)"
          >全选当前</el-button
        >
        <el-button size="small" @click.stop="toggleSelectAll(false)"
          >清空当前</el-button
        >
      </div>

      <el-empty
        v-if="filteredTables.length === 0"
        :description="`未找到匹配的${itemLabel}`"
      />

      <div
        v-for="table in filteredTables"
        :key="table.index"
        class="table-item"
        :class="{
          selected: props.multiple
            ? selectedIndexes.includes(table.index)
            : selectedIndex === table.index
        }"
        role="button"
        tabindex="0"
        @click="handleSelect(table)"
        @keydown.enter="handleSelect(table)"
        @keydown.space.prevent="handleSelect(table)"
      >
        <div class="table-header">
          <div class="table-title">
            <span class="table-index">
              {{ itemLabel }} {{ table.index + 1 }}
              <span v-if="table.name" class="table-name"
                >（{{ table.name }}）</span
              >
            </span>
            <span class="table-meta"
              >（{{ table.rowCount }} 行 × {{ table.columnCount }} 列）</span
            >
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
          <el-icon style="color: var(--color-primary)">
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

.table-list-actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

.table-search {
  max-width: 360px;
}

.bulk-actions {
  display: flex;
  gap: 10px;
  align-items: center;
  padding: 10px 12px;
  background: var(--app-info-bg);
  border: 1px solid var(--app-border);
  border-radius: 8px;
}

.bulk-tip {
  margin-right: auto;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.table-item {
  position: relative;
  padding: 16px;
  cursor: pointer;
  border: 2px solid var(--app-border);
  border-radius: 12px;
  transition: all 0.2s;
}

.table-item:hover {
  background-color: var(--app-fill-hover);
  border-color: var(--app-primary);
}

.table-item.selected {
  background-color: var(--app-primary-light);
  border-color: var(--color-primary);
}

.table-list.compact .table-item {
  padding: 10px 12px;
  border-width: 1px;
}

.table-list.compact .table-header {
  margin-bottom: 4px;
}

.table-list.compact .table-index {
  font-size: 14px;
}

.table-list.compact .table-headers {
  margin-bottom: 0;
  overflow: hidden;
  white-space: nowrap;
}

.table-list.compact .table-headers .headers {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
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
  color: var(--color-text);
}

.table-name {
  margin-left: 4px;
  font-weight: 400;
  color: var(--app-text-secondary);
}

.table-title {
  display: flex;
  gap: 8px;
  align-items: baseline;
  min-width: 0;
}

.table-meta {
  font-size: 13px;
  color: var(--app-text-secondary);
  white-space: nowrap;
}

.table-tags {
  display: flex;
  gap: 8px;
}

.table-headers {
  display: flex;
  align-items: flex-start;
  margin-bottom: 8px;
  font-size: 13px;
  color: var(--app-text-secondary);
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
