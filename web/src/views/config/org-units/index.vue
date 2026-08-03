<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  createOrgUnit,
  deleteOrgUnit,
  getOrgUnitTree,
  moveOrgUnit,
  updateOrgUnit,
  type OrgUnit
} from "@/api/org-unit";
import { hasPerms } from "@/utils/auth";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import {
  requiredSelectionRule,
  requiredTrimmedRule,
  validateForm
} from "@/utils/form-rules";
import { isMessageBoxCancel } from "@/utils/message-box";
import {
  getAllowedChildTypes,
  orgUnitTypeLabels,
  type OrgUnitType
} from "./hierarchy";
import {
  countOrgUnitSubtree,
  getOrgUnitMoveTargetDisabledReason
} from "./move";

defineOptions({
  name: "OrgUnitsConfig"
});

const loading = ref(false);
const submitting = ref(false);
const treeData = ref<OrgUnit[]>([]);
const createDialogVisible = ref(false);
const editDialogVisible = ref(false);
const moveDialogVisible = ref(false);

const createForm = reactive({
  parentId: null as number | null,
  parentName: "",
  unitType: null as number | null,
  code: "",
  name: "",
  sort: 0,
  isActive: true
});
const editForm = reactive({
  id: 0,
  isRoot: false,
  code: "",
  name: "",
  sort: 0,
  isActive: true
});
const moveForm = reactive({
  sourceId: 0,
  newParentId: null as number | null
});
const createFormRef = ref<FormInstance>();
const editFormRef = ref<FormInstance>();
const createFormRules: FormRules<typeof createForm> = {
  unitType: [requiredSelectionRule("请选择组织类型")],
  code: [requiredTrimmedRule("请输入组织编码")],
  name: [requiredTrimmedRule("请输入组织名称")]
};
const editFormRules: FormRules<typeof editForm> = {
  code: [requiredTrimmedRule("请输入组织编码")],
  name: [requiredTrimmedRule("请输入组织名称")]
};

const canCreate = computed(() => hasPerms("btn:org-unit:create"));
const canUpdate = computed(() => hasPerms("btn:org-unit:update"));
const canDelete = computed(() => hasPerms("btn:org-unit:delete"));
const canMove = computed(() => hasPerms("btn:org-unit:move"));

const flatNodes = computed(() => {
  const nodes: OrgUnit[] = [];
  const visit = (items: OrgUnit[]) => {
    items.forEach(item => {
      nodes.push(item);
      visit(item.children ?? []);
    });
  };
  visit(treeData.value);
  return nodes;
});
const activeCount = computed(
  () => flatNodes.value.filter(item => item.isActive).length
);
const selectedParent = computed(() =>
  flatNodes.value.find(item => item.id === createForm.parentId)
);
const childTypeOptions = computed(() => {
  const parentType = (selectedParent.value?.unitType ?? 3) as OrgUnitType;
  return getAllowedChildTypes(parentType).map(value => ({
    value,
    label: orgUnitTypeLabels[value]
  }));
});

type MoveTargetNode = OrgUnit & {
  moveDisabledReason: string | null;
  children?: MoveTargetNode[];
};

const moveSource = computed(() =>
  flatNodes.value.find(item => item.id === moveForm.sourceId)
);
const moveCurrentParent = computed(() =>
  flatNodes.value.find(item => item.id === moveSource.value?.parentId)
);
const moveSelectedParent = computed(() =>
  flatNodes.value.find(item => item.id === moveForm.newParentId)
);
const moveAffectedCount = computed(() =>
  moveSource.value ? countOrgUnitSubtree(moveSource.value) : 0
);
const moveTargetTree = computed<MoveTargetNode[]>(() => {
  const source = moveSource.value;
  if (!source) return [];

  const mapNode = (target: OrgUnit): MoveTargetNode => ({
    ...target,
    moveDisabledReason: getOrgUnitMoveTargetDisabledReason(
      source,
      target,
      source.parentId ?? null
    ),
    children: (target.children ?? []).map(mapNode)
  });
  return treeData.value.map(mapNode);
});

const loadTree = async () => {
  loading.value = true;
  try {
    const res = await getOrgUnitTree();
    if (res.code === 0) {
      treeData.value = res.data ?? [];
    } else {
      ElMessage.error(res.message || "加载组织失败");
    }
  } catch (error) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "加载组织失败"));
    }
  } finally {
    loading.value = false;
  }
};

const openCreateDialog = (parent: OrgUnit) => {
  createForm.parentId = parent.id;
  createForm.parentName = `${parent.name} (${parent.code})`;
  createForm.unitType = Math.min(parent.unitType + 1, 3);
  createForm.code = "";
  createForm.name = "";
  createForm.sort = 0;
  createForm.isActive = true;
  createDialogVisible.value = true;
};

const openEditDialog = (row: OrgUnit) => {
  editForm.id = row.id;
  editForm.isRoot = row.parentId == null && row.unitType === 0;
  editForm.code = row.code;
  editForm.name = row.name;
  editForm.sort = row.sort;
  editForm.isActive = row.isActive;
  editDialogVisible.value = true;
};

const openMoveDialog = (row: OrgUnit) => {
  moveForm.sourceId = row.id;
  moveForm.newParentId = null;
  moveDialogVisible.value = true;
};

const handleCreate = async () => {
  if (!(await validateForm(createFormRef.value))) return;
  if (!createForm.parentId || createForm.unitType == null) return;

  submitting.value = true;
  try {
    const res = await createOrgUnit({
      parentId: createForm.parentId,
      unitType: createForm.unitType,
      code: createForm.code.trim(),
      name: createForm.name.trim(),
      sort: createForm.sort,
      isActive: createForm.isActive
    });
    if (res.code === 0) {
      ElMessage.success("新增组织成功");
      createDialogVisible.value = false;
      await loadTree();
    } else {
      ElMessage.error(res.message || "新增组织失败");
    }
  } catch (error) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "新增组织失败"));
    }
  } finally {
    submitting.value = false;
  }
};

const handleUpdate = async () => {
  if (!(await validateForm(editFormRef.value))) return;

  submitting.value = true;
  try {
    const res = await updateOrgUnit(editForm.id, {
      code: editForm.code.trim(),
      name: editForm.name.trim(),
      sort: editForm.sort,
      isActive: editForm.isActive
    });
    if (res.code === 0) {
      ElMessage.success("更新组织成功");
      editDialogVisible.value = false;
      await loadTree();
    } else {
      ElMessage.error(res.message || "更新组织失败");
    }
  } catch (error) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "更新组织失败"));
    }
  } finally {
    submitting.value = false;
  }
};

const handleDelete = async (row: OrgUnit) => {
  try {
    await ElMessageBox.confirm(
      `确定删除组织“${row.name}”吗？仅无下级且未被用户、角色或业务数据引用的节点可以删除。`,
      "删除组织",
      {
        confirmButtonText: "删除",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
    const res = await deleteOrgUnit(row.id);
    if (res.code === 0) {
      ElMessage.success("删除组织成功");
      await loadTree();
    } else {
      ElMessage.error(res.message || "删除组织失败");
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除组织失败"));
  }
};

const handleMoveTargetClick = (target: MoveTargetNode) => {
  if (target.moveDisabledReason) return;
  moveForm.newParentId = target.id;
};

const handleMove = async () => {
  const source = moveSource.value;
  if (!source || !moveForm.newParentId) {
    ElMessage.warning("请选择新的上级组织");
    return;
  }

  submitting.value = true;
  try {
    const res = await moveOrgUnit(source.id, {
      newParentId: moveForm.newParentId
    });
    if (res.code === 0) {
      ElMessage.success("组织及其下级已移动");
      moveDialogVisible.value = false;
      await loadTree();
    } else {
      ElMessage.error(res.message || "移动组织失败");
    }
  } catch (error) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "移动组织失败"));
    }
  } finally {
    submitting.value = false;
  }
};

const canAddChild = (row: OrgUnit) =>
  canCreate.value &&
  row.isActive &&
  getAllowedChildTypes(row.unitType as OrgUnitType).length > 0;
const isRoot = (row: OrgUnit) => row.parentId == null && row.unitType === 0;

onMounted(loadTree);
</script>

<template>
  <div class="page config-page">
    <el-card class="full-height-table-wrapper org-card">
      <template #header>
        <div class="org-header">
          <div>
            <div class="org-header__title">组织架构</div>
            <div class="org-header__meta">
              共 {{ flatNodes.length }} 个节点，{{ activeCount }} 个启用
            </div>
          </div>
          <el-button :loading="loading" @click="loadTree">刷新</el-button>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="层级规则"
        description="公司为唯一根节点；下级可按事业部、部门、课别向下跳级创建，也可将现有节点及其整棵下级移动到新的有效上级。用户和业务数据仍保留原组织 ID。"
        class="org-rule"
      />

      <el-table
        v-loading="loading"
        :data="treeData"
        row-key="id"
        default-expand-all
        :tree-props="{ children: 'children' }"
        stripe
      >
        <el-table-column prop="name" label="组织名称" min-width="260">
          <template #default="{ row }">
            <span class="org-name">{{ row.name }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="code" label="编码" min-width="150">
          <template #default="{ row }">
            <code class="org-code">{{ row.code }}</code>
          </template>
        </el-table-column>
        <el-table-column label="类型" width="110">
          <template #default="{ row }">
            <el-tag effect="plain">
              {{ orgUnitTypeLabels[row.unitType as OrgUnitType] }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="sort" label="排序" width="80" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'">
              {{ row.isActive ? "启用" : "停用" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="280" fixed="right">
          <template #default="{ row }">
            <el-button
              v-if="canAddChild(row)"
              v-perms="'btn:org-unit:create'"
              type="primary"
              link
              @click="openCreateDialog(row)"
            >
              新增下级
            </el-button>
            <el-button
              v-if="canUpdate"
              v-perms="'btn:org-unit:update'"
              type="primary"
              link
              @click="openEditDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canMove && !isRoot(row)"
              v-perms="'btn:org-unit:move'"
              type="primary"
              link
              @click="openMoveDialog(row)"
            >
              移动
            </el-button>
            <el-button
              v-if="canDelete && !isRoot(row)"
              v-perms="'btn:org-unit:delete'"
              type="danger"
              link
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-empty
        v-if="!loading && treeData.length === 0"
        description="未找到组织数据"
      />
    </el-card>

    <el-dialog
      v-model="createDialogVisible"
      title="新增下级组织"
      width="min(520px, calc(100vw - 32px))"
      destroy-on-close
    >
      <el-form
        ref="createFormRef"
        :model="createForm"
        :rules="createFormRules"
        label-width="100px"
        status-icon
      >
        <el-form-item label="上级组织">
          <el-input :model-value="createForm.parentName" disabled />
        </el-form-item>
        <el-form-item label="组织类型" prop="unitType">
          <el-select v-model="createForm.unitType" class="w-full">
            <el-option
              v-for="option in childTypeOptions"
              :key="option.value"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="组织编码" prop="code">
          <el-input
            v-model="createForm.code"
            maxlength="64"
            placeholder="保存后自动转为大写"
          />
        </el-form-item>
        <el-form-item label="组织名称" prop="name">
          <el-input v-model="createForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="createForm.sort" :min="0" class="w-full" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="createForm.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button
          v-perms="'btn:org-unit:create'"
          type="primary"
          :loading="submitting"
          @click="handleCreate"
        >
          创建
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="editDialogVisible"
      title="编辑组织"
      width="min(520px, calc(100vw - 32px))"
    >
      <el-form
        ref="editFormRef"
        :model="editForm"
        :rules="editFormRules"
        label-width="100px"
        status-icon
      >
        <el-form-item label="组织编码" prop="code">
          <el-input v-model="editForm.code" maxlength="64" />
        </el-form-item>
        <el-form-item label="组织名称" prop="name">
          <el-input v-model="editForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="editForm.sort" :min="0" class="w-full" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="editForm.isActive" :disabled="editForm.isRoot" />
          <span v-if="editForm.isRoot" class="root-hint">
            公司根节点必须保持启用
          </span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button
          v-perms="'btn:org-unit:update'"
          type="primary"
          :loading="submitting"
          @click="handleUpdate"
        >
          保存
        </el-button>
      </template>
    </el-dialog>

    <el-dialog
      v-model="moveDialogVisible"
      title="移动组织"
      width="min(640px, calc(100vw - 32px))"
      destroy-on-close
    >
      <el-descriptions :column="1" border class="move-summary">
        <el-descriptions-item label="移动节点">
          {{ moveSource?.name }}（{{ moveSource?.code }}）
        </el-descriptions-item>
        <el-descriptions-item label="当前上级">
          {{ moveCurrentParent?.name ?? "-" }}
        </el-descriptions-item>
        <el-descriptions-item label="新的上级">
          <span :class="{ 'move-placeholder': !moveSelectedParent }">
            {{ moveSelectedParent?.name ?? "请在下方组织树中选择" }}
          </span>
        </el-descriptions-item>
      </el-descriptions>

      <div class="move-target-label">选择新的上级组织</div>
      <div class="move-target-tree">
        <el-tree
          :data="moveTargetTree"
          node-key="id"
          default-expand-all
          :expand-on-click-node="false"
          @node-click="handleMoveTargetClick"
        >
          <template #default="{ data }">
            <div
              class="move-target-node"
              :class="{
                'is-disabled': data.moveDisabledReason,
                'is-selected': data.id === moveForm.newParentId
              }"
            >
              <span class="move-target-node__identity">
                <span>{{ data.name }}</span>
                <code>{{ data.code }}</code>
                <el-tag size="small" effect="plain">
                  {{ orgUnitTypeLabels[data.unitType as OrgUnitType] }}
                </el-tag>
              </span>
              <span
                v-if="data.moveDisabledReason"
                class="move-target-node__reason"
              >
                {{ data.moveDisabledReason }}
              </span>
            </div>
          </template>
        </el-tree>
      </div>

      <el-alert
        type="warning"
        :closable="false"
        show-icon
        :title="`将同时移动 ${moveAffectedCount} 个组织节点`"
        description="移动后，按组织子树计算的数据可见范围会立即变化；用户归属、验收规格和历史文件仍保留原组织 ID。"
        class="move-warning"
      />

      <template #footer>
        <el-button @click="moveDialogVisible = false">取消</el-button>
        <el-button
          v-perms="'btn:org-unit:move'"
          type="primary"
          :loading="submitting"
          :disabled="!moveForm.newParentId"
          @click="handleMove"
        >
          确认移动
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 0;
}

.org-card {
  --org-accent: #0b4f8a;
}

.org-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.org-header__title {
  font-size: 17px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.org-header__meta {
  margin-top: 4px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.org-rule {
  margin-bottom: 16px;
}

.org-name {
  font-weight: 550;
}

.org-code {
  padding: 2px 6px;
  font-size: 12px;
  color: var(--org-accent);
  background: color-mix(in srgb, var(--org-accent) 7%, transparent);
  border-radius: 4px;
}

.root-hint {
  margin-left: 10px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.move-summary {
  margin-bottom: 16px;
}

.move-placeholder {
  color: var(--app-text-secondary);
}

.move-target-label {
  margin-bottom: 8px;
  font-size: 14px;
  font-weight: 600;
  color: var(--app-text-primary);
}

.move-target-tree {
  max-height: 300px;
  padding: 8px;
  overflow: auto;
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
}

.move-target-node {
  display: flex;
  flex: 1;
  align-items: center;
  justify-content: space-between;
  min-width: 0;
  padding: 4px 8px;
  border-radius: 3px;
}

.move-target-node.is-selected {
  color: var(--org-accent);
  background: color-mix(in srgb, var(--org-accent) 10%, transparent);
}

.move-target-node.is-disabled {
  color: var(--app-text-secondary);
  cursor: not-allowed;
  opacity: 0.68;
}

.move-target-node__identity {
  display: flex;
  gap: 8px;
  align-items: center;
  min-width: 0;
}

.move-target-node__identity code {
  font-size: 11px;
}

.move-target-node__reason {
  margin-left: 12px;
  font-size: 12px;
  white-space: nowrap;
}

.move-warning {
  margin-top: 16px;
}
</style>
