<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount, nextTick } from "vue";
import { ElMessage } from "element-plus";
import { getSpecGroups, type SpecGroup } from "@/api/spec";
import SpecGroupTree from "./components/SpecGroupTree.vue";
import SpecTable from "./components/SpecTable.vue";
import type { SelectedGroup } from "./components/SpecGroupTree.vue";

defineOptions({
  name: "AcceptanceSpecs"
});

// 分组数据
const groups = ref<SpecGroup[]>([]);
const groupsLoading = ref(false);

// 当前选中分组
const selectedGroup = ref<SelectedGroup | null>(null);
const pageRef = ref<HTMLElement | null>(null);
const pageViewportHeight = ref(0);
let appMainWrapEl: HTMLElement | null = null;
let previousAppMainOverflowY = "";

/** 锁定页面可视高度，避免外层滚动，滚动仅发生在表格内容区 */
const updatePageViewportHeight = () => {
  const host = pageRef.value;
  if (!host) return;
  const rect = host.getBoundingClientRect();
  pageViewportHeight.value = Math.max(480, Math.floor(window.innerHeight - rect.top - 12));
};

/** 锁定外层滚动，只允许本页内部滚动 */
const lockOuterScroll = () => {
  appMainWrapEl = document.querySelector(".app-main .el-scrollbar__wrap") as HTMLElement | null;
  if (!appMainWrapEl) return;
  previousAppMainOverflowY = appMainWrapEl.style.overflowY;
  appMainWrapEl.style.overflowY = "hidden";
};

/** 恢复外层滚动 */
const unlockOuterScroll = () => {
  if (!appMainWrapEl) return;
  appMainWrapEl.style.overflowY = previousAppMainOverflowY;
  appMainWrapEl = null;
};

/** 加载分组汇总 */
const loadGroups = async () => {
  groupsLoading.value = true;
  try {
    const res = await getSpecGroups();
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

onMounted(() => {
  loadGroups();
  nextTick(updatePageViewportHeight);
  nextTick(lockOuterScroll);
  window.addEventListener("resize", updatePageViewportHeight);
});

onBeforeUnmount(() => {
  window.removeEventListener("resize", updatePageViewportHeight);
  unlockOuterScroll();
});
</script>

<template>
  <div
    ref="pageRef"
    class="page"
    :style="pageViewportHeight > 0 ? { height: `${pageViewportHeight}px` } : undefined"
  >
    <div class="page-header">
      <div>
        <div class="page-title">验收规格</div>
        <div class="page-subtitle">管理验收规格条目与详情</div>
      </div>
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
          v-if="selectedGroup"
          :customer-id="selectedGroup.customerId"
          :machine-model-id="selectedGroup.machineModelId"
          :process-id="selectedGroup.processId"
          :customer-name="selectedGroup.customerName"
          :machine-model-name="selectedGroup.machineModelName"
          :process-name="selectedGroup.processName"
          @data-change="handleDataChange"
        />
        <el-empty
          v-else
          description="请在左侧选择一个分组以查看规格数据"
          :image-size="120"
        />
      </el-card>
    </div>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-height: 0;
  overflow: hidden;
}

.split-layout {
  display: flex;
  gap: 16px;
  flex: 1;
  min-height: 0;
  align-items: stretch;
}

.left-panel {
  width: 300px;
  flex-shrink: 0;
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.left-panel :deep(.el-card__body) {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.right-panel {
  flex: 1;
  min-width: 0;
  height: 100%;
  overflow: hidden;
}

.right-panel :deep(.el-card__body) {
  height: 100%;
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

@media (max-width: 992px) {
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
    height: auto;
    overflow: visible;
    display: block;
  }
}
</style>
