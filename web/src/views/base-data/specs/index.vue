<script setup lang="ts">
import { computed, ref, onMounted } from "vue";
import { ElMessage } from "element-plus";
import { getSpecGroups, type SpecGroup } from "@/api/spec";
import { getBusinessOrgContext, type BusinessOrgContext } from "@/api/org-unit";
import SpecGroupTree from "./components/SpecGroupTree.vue";
import SpecTable from "./components/SpecTable.vue";
import type { SelectedGroup } from "./components/SpecGroupTree.vue";

defineOptions({
  name: "AcceptanceSpecs"
});

// 分组数据
const groups = ref<SpecGroup[]>([]);
const groupsLoading = ref(false);
const orgContextLoading = ref(false);
const businessOrgContext = ref<BusinessOrgContext | null>(null);
const selectedOrgUnitId = ref<number>();

// 当前选中分组
const selectedGroup = ref<SelectedGroup | null>(null);
const queryOrgUnitId = computed(() =>
  businessOrgContext.value?.requiresSelection
    ? selectedOrgUnitId.value
    : undefined
);
const currentScopeLabel = computed(() => {
  const context = businessOrgContext.value;
  if (!context) return "加载中";
  if (!context.requiresSelection) {
    return context.currentOrgUnitName || "当前部门";
  }
  if (!selectedOrgUnitId.value) return "公司总体";
  return (
    context.options.find(item => item.id === selectedOrgUnitId.value)?.name ||
    "所选部门"
  );
});

const loadBusinessOrgContext = async () => {
  orgContextLoading.value = true;
  try {
    const res = await getBusinessOrgContext();
    if (res.code !== 0) {
      ElMessage.error(res.message);
      return false;
    }
    businessOrgContext.value = res.data;
    selectedOrgUnitId.value = res.data.requiresSelection
      ? undefined
      : res.data.currentOrgUnitId;
    return true;
  } catch {
    ElMessage.error("加载部门范围失败");
    return false;
  } finally {
    orgContextLoading.value = false;
  }
};

/** 加载分组汇总 */
const loadGroups = async () => {
  groupsLoading.value = true;
  try {
    const res = await getSpecGroups(
      queryOrgUnitId.value ? { orgUnitId: queryOrgUnitId.value } : undefined
    );
    if (res.code === 0) {
      groups.value = res.data;
      // 如果当前选中分组已不存在（被删光），自动清除选中
      if (selectedGroup.value) {
        const sg = selectedGroup.value;
        const exists = groups.value.some(
          g =>
            g.customerId === sg.customerId &&
            g.machineModelId === sg.machineModelId &&
            g.processId === sg.processId
        );
        if (!exists) {
          selectedGroup.value = null;
        }
      }
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载分组数据失败");
  } finally {
    groupsLoading.value = false;
  }
};

/** 选中分组 */
const handleGroupSelect = (group: SelectedGroup) => {
  selectedGroup.value = group;
};

/** 子表格数据变更后刷新分组 */
const handleDataChange = () => {
  loadGroups();
};

const handleOrgScopeChange = () => {
  selectedGroup.value = null;
  void loadGroups();
};

onMounted(async () => {
  if (await loadBusinessOrgContext()) {
    await loadGroups();
  }
});
</script>

<template>
  <div class="page page--fill specs-page">
    <div class="scope-bar">
      <div class="scope-bar__main">
        <span class="scope-bar__label">数据范围</span>
        <el-select
          v-if="businessOrgContext?.requiresSelection"
          v-model="selectedOrgUnitId"
          :loading="orgContextLoading"
          placeholder="公司总体"
          clearable
          class="scope-bar__select"
          @change="handleOrgScopeChange"
        >
          <el-option
            v-for="option in businessOrgContext.options"
            :key="option.id"
            :label="option.name"
            :value="option.id"
          />
        </el-select>
        <el-tag v-else-if="businessOrgContext" effect="plain" type="info">
          {{ businessOrgContext.currentOrgUnitName }}
        </el-tag>
      </div>
      <span class="scope-bar__hint">
        分组、列表和 AI 搜索均按“{{ currentScopeLabel }}”显示
      </span>
    </div>

    <div class="split-layout">
      <!-- 左侧面板：分组树 -->
      <el-card class="left-panel">
        <SpecGroupTree
          :groups="groups"
          :loading="groupsLoading"
          @select="handleGroupSelect"
        />
      </el-card>

      <!-- 右侧面板：规格表格 -->
      <el-card class="right-panel">
        <SpecTable
          :customer-id="selectedGroup?.customerId"
          :machine-model-id="selectedGroup?.machineModelId"
          :process-id="selectedGroup?.processId"
          :customer-name="selectedGroup?.customerName"
          :machine-model-name="selectedGroup?.machineModelName"
          :process-name="selectedGroup?.processName"
          :org-unit-id="queryOrgUnitId"
          :business-org-options="businessOrgContext?.options ?? []"
          :requires-business-org-selection="
            businessOrgContext?.requiresSelection ?? false
          "
          :current-org-unit-id="businessOrgContext?.currentOrgUnitId"
          :scope-label="currentScopeLabel"
          @data-change="handleDataChange"
        />
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 0;
  padding: 0;
  overflow: hidden;
}

.scope-bar {
  display: flex;
  flex-shrink: 0;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  min-height: 48px;
  padding: 7px 12px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 4px;
}

.scope-bar__main {
  display: flex;
  gap: 10px;
  align-items: center;
}

.scope-bar__label {
  font-size: 14px;
  font-weight: 600;
  color: var(--app-text-primary);
  white-space: nowrap;
}

.scope-bar__select {
  width: 220px;
}

.scope-bar__hint {
  font-size: 13px;
  color: var(--app-text-secondary);
}

.split-layout {
  display: flex;
  flex: 1;
  gap: 16px;
  align-items: stretch;
  min-height: 0;
}

.left-panel {
  display: flex;
  flex-shrink: 0;
  flex-direction: column;
  width: 300px;
  height: 100%;
  overflow: hidden;
}

.left-panel :deep(.el-card__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  overflow: hidden;
}

.right-panel {
  flex: 1;
  min-width: 0;
  height: 100%;
  overflow: hidden;
}

.right-panel :deep(.el-card__body) {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}

@media (width <= 992px) {
  .scope-bar {
    flex-direction: column;
    gap: 6px;
    align-items: flex-start;
  }

  .split-layout {
    flex-direction: column;
    height: auto !important;
    overflow: auto;
  }

  .left-panel {
    width: 100%;
    height: auto;
  }

  .right-panel {
    height: auto;
  }

  .right-panel :deep(.el-card__body) {
    display: block;
    height: auto;
    overflow: visible;
  }
}
</style>
