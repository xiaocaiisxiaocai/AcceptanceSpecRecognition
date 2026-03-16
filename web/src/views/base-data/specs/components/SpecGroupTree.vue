<script setup lang="ts">
import { ref, computed, watch } from "vue";
import { Search } from "@element-plus/icons-vue";
import type { SpecGroup } from "@/api/spec";
import type ElTree from "element-plus/es/components/tree";

/** 选中分组的信息 */
export interface SelectedGroup {
  customerId: number;
  customerName: string;
  machineModelId?: number;
  machineModelName?: string;
  processId?: number;
  processName?: string;
}

/** 树节点数据 */
interface TreeNode {
  id: string;
  label: string;
  count: number;
  isLeaf: boolean;
  data?: SelectedGroup;
  children?: TreeNode[];
}

const props = defineProps<{
  groups: SpecGroup[];
  loading?: boolean;
}>();

const emit = defineEmits<{
  select: [group: SelectedGroup];
}>();

const filterText = ref("");
const treeRef = ref<InstanceType<typeof ElTree>>();
const currentNodeKey = ref<string>("");

/** 将扁平的分组数据构建为三级树：客户 → 机型 → 制程 */
const treeData = computed<TreeNode[]>(() => {
  const customerMap = new Map<
    number,
    {
      name: string;
      machineModels: Map<
        string,
        {
          id: number | undefined;
          name: string;
          processes: { id: number | undefined; name: string; count: number }[];
        }
      >;
      totalCount: number;
    }
  >();

  for (const g of props.groups) {
    if (!customerMap.has(g.customerId)) {
      customerMap.set(g.customerId, {
        name: g.customerName,
        machineModels: new Map(),
        totalCount: 0
      });
    }
    const customer = customerMap.get(g.customerId)!;
    customer.totalCount += g.specCount;

    const mmKey = g.machineModelId != null ? String(g.machineModelId) : "__null__";
    if (!customer.machineModels.has(mmKey)) {
      customer.machineModels.set(mmKey, {
        id: g.machineModelId ?? undefined,
        name: g.machineModelName || "未指定机型",
        processes: []
      });
    }
    customer.machineModels.get(mmKey)!.processes.push({
      id: g.processId ?? undefined,
      name: g.processName || "未指定制程",
      count: g.specCount
    });
  }

  const nodes: TreeNode[] = [];

  for (const [customerId, customer] of customerMap) {
    const customerNode: TreeNode = {
      id: `c-${customerId}`,
      label: customer.name,
      count: customer.totalCount,
      isLeaf: false,
      children: []
    };

    for (const [, mm] of customer.machineModels) {
      // 如果只有一个制程，合并机型+制程为一个叶子节点
      if (mm.processes.length === 1) {
        const p = mm.processes[0];
        const leafLabel =
          mm.name !== "未指定机型" || p.name !== "未指定制程"
            ? `${mm.name} / ${p.name}`
            : "未指定机型 / 未指定制程";
        customerNode.children!.push({
          id: `leaf-${customerId}-${mm.id ?? "null"}-${p.id ?? "null"}`,
          label: leafLabel,
          count: p.count,
          isLeaf: true,
          data: {
            customerId,
            customerName: customer.name,
            machineModelId: mm.id,
            machineModelName: mm.id != null ? mm.name : undefined,
            processId: p.id,
            processName: p.id != null ? p.name : undefined
          }
        });
      } else {
        // 多个制程 → 机型作为中间节点
        const mmCount = mm.processes.reduce((sum, p) => sum + p.count, 0);
        const mmNode: TreeNode = {
          id: `mm-${customerId}-${mm.id ?? "null"}`,
          label: mm.name,
          count: mmCount,
          isLeaf: false,
          children: mm.processes.map(p => ({
            id: `leaf-${customerId}-${mm.id ?? "null"}-${p.id ?? "null"}`,
            label: p.name,
            count: p.count,
            isLeaf: true,
            data: {
              customerId,
              customerName: customer.name,
              machineModelId: mm.id,
              machineModelName: mm.id != null ? mm.name : undefined,
              processId: p.id,
              processName: p.id != null ? p.name : undefined
            }
          }))
        };
        customerNode.children!.push(mmNode);
      }
    }

    nodes.push(customerNode);
  }

  return nodes;
});

/** 过滤节点 */
const filterNode = (value: string, data: TreeNode) => {
  if (!value) return true;
  return data.label.toLowerCase().includes(value.toLowerCase());
};

/** 搜索变化时触发过滤 */
watch(filterText, val => {
  treeRef.value?.filter(val);
});

/** 点击节点 */
const handleNodeClick = (data: TreeNode) => {
  if (data.isLeaf && data.data) {
    currentNodeKey.value = data.id;
    emit("select", data.data);
  }
};

defineExpose({ currentNodeKey });
</script>

<template>
  <div class="spec-group-tree">
    <div class="tree-header">数据分组</div>
    <el-input
      v-model="filterText"
      placeholder="搜索分组..."
      clearable
      :prefix-icon="Search"
      class="tree-search"
    />
    <div v-loading="loading" class="tree-body">
      <el-tree
        ref="treeRef"
        :data="treeData"
        :props="{ children: 'children', label: 'label' }"
        node-key="id"
        highlight-current
        default-expand-all
        :expand-on-click-node="false"
        :filter-node-method="filterNode as any"
        @node-click="handleNodeClick"
      >
        <template #default="{ data }">
          <span class="tree-node">
            <span class="tree-node-label" :class="{ 'is-leaf': data.isLeaf }">
              {{ data.label }}
            </span>
            <el-badge
              :value="data.count"
              :max="999999999"
              type="info"
              class="tree-node-badge"
            />
          </span>
        </template>
      </el-tree>
      <el-empty
        v-if="!loading && treeData.length === 0"
        description="暂无分组数据"
        :image-size="60"
      />
    </div>
  </div>
</template>

<style scoped>
.spec-group-tree {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.tree-header {
  font-size: 16px;
  font-weight: 600;
  padding: 0 0 12px;
}

.tree-search {
  margin-bottom: 12px;
}

.tree-body {
  flex: 1;
  overflow-y: auto;
}

.tree-node {
  display: flex;
  align-items: center;
  justify-content: space-between;
  flex: 1;
  overflow: hidden;
  padding-right: 8px;
}

.tree-node-label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.tree-node-label.is-leaf {
  cursor: pointer;
}

.tree-node-badge {
  flex-shrink: 0;
  margin-left: 8px;
}

/* 覆盖 el-badge 样式让它更小巧 */
.tree-node-badge :deep(.el-badge__content) {
  font-size: 11px;
  height: 18px;
  line-height: 18px;
  padding: 0 5px;
}
</style>
