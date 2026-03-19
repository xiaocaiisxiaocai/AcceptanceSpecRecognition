<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  createOrgUnit,
  deleteOrgUnit,
  getOrgUnitTree,
  type OrgUnit,
  updateOrgUnit
} from "@/api/org-unit";

defineOptions({
  name: "OrgUnitsConfig"
});

const loading = ref(false);
const treeData = ref<OrgUnit[]>([]);
const deepestUnitType = 3;
const unitTypeOptions = [
  { label: "公司", value: 0 },
  { label: "事业部", value: 1 },
  { label: "部门", value: 2 },
  { label: "课别", value: 3 }
];

const createDialogVisible = ref(false);
const editDialogVisible = ref(false);

const createForm = reactive({
  parentId: null as number | null,
  unitType: 1,
  code: "",
  name: "",
  sort: 0,
  isActive: true
});

const editForm = reactive({
  id: 0,
  code: "",
  name: "",
  sort: 0,
  isActive: true
});

const loadTree = async () => {
  loading.value = true;
  try {
    const res = await getOrgUnitTree();
    if (res.code === 0) {
      treeData.value = res.data || [];
    } else {
      ElMessage.error(res.message || "加载组织失败");
    }
  } catch {
    ElMessage.error("加载组织失败");
  } finally {
    loading.value = false;
  }
};

const flattenOrgUnits = (nodes: OrgUnit[]): OrgUnit[] => {
  const all: OrgUnit[] = [];
  const walk = (items: OrgUnit[]) => {
    items.forEach(item => {
      all.push(item);
      if (item.children?.length) {
        walk(item.children);
      }
    });
  };
  walk(nodes);
  return all;
};

const flatOrgUnits = computed(() => flattenOrgUnits(treeData.value));

const rootOrgUnit = computed(() => {
  return (
    flatOrgUnits.value.find(node => node.parentId == null && node.unitType === 0) ??
    null
  );
});

const parentOptions = computed(() => {
  return flatOrgUnits.value.filter(node => node.unitType < deepestUnitType);
});

const findOrgUnit = (id: number | null | undefined) => {
  if (id == null) return null;
  return flatOrgUnits.value.find(node => node.id === id) ?? null;
};

const canCreateChild = (row: OrgUnit) => row.unitType < deepestUnitType;

const createUnitTypeOptions = computed(() => {
  const parent = findOrgUnit(createForm.parentId);
  if (!parent) {
    return rootOrgUnit.value ? [] : unitTypeOptions.filter(item => item.value === 0);
  }

  return unitTypeOptions.filter(item => item.value > parent.unitType);
});

const syncCreateUnitType = () => {
  const options = createUnitTypeOptions.value;
  if (!options.length) return;

  if (!options.some(option => option.value === createForm.unitType)) {
    createForm.unitType = options[0].value;
  }
};

const openCreateDialog = (parentId?: number | null) => {
  const targetParentId = parentId ?? rootOrgUnit.value?.id ?? null;
  const targetParent = findOrgUnit(targetParentId);
  if (targetParent && !canCreateChild(targetParent)) {
    ElMessage.warning("课别节点不能新增下级组织");
    return;
  }

  createForm.parentId = targetParentId;
  createForm.unitType = 1;
  createForm.code = "";
  createForm.name = "";
  createForm.sort = 0;
  createForm.isActive = true;
  syncCreateUnitType();
  createDialogVisible.value = true;
};

const openEditDialog = (row: OrgUnit) => {
  editForm.id = row.id;
  editForm.code = row.code;
  editForm.name = row.name;
  editForm.sort = row.sort;
  editForm.isActive = row.isActive;
  editDialogVisible.value = true;
};

const handleCreate = async () => {
  if (!createForm.code.trim() || !createForm.name.trim()) {
    ElMessage.warning("编码和名称不能为空");
    return;
  }
  if (!createUnitTypeOptions.value.length) {
    ElMessage.warning("当前上级组织不允许新增下级节点");
    return;
  }
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
      ElMessage.success("创建成功");
      createDialogVisible.value = false;
      await loadTree();
    } else {
      ElMessage.error(res.message || "创建失败");
    }
  } catch {
    ElMessage.error("创建失败");
  }
};

const handleUpdate = async () => {
  if (!editForm.code.trim() || !editForm.name.trim()) {
    ElMessage.warning("编码和名称不能为空");
    return;
  }
  try {
    const res = await updateOrgUnit(editForm.id, {
      code: editForm.code.trim(),
      name: editForm.name.trim(),
      sort: editForm.sort,
      isActive: editForm.isActive
    });
    if (res.code === 0) {
      ElMessage.success("更新成功");
      editDialogVisible.value = false;
      await loadTree();
    } else {
      ElMessage.error(res.message || "更新失败");
    }
  } catch {
    ElMessage.error("更新失败");
  }
};

const handleDelete = async (row: OrgUnit) => {
  try {
    await ElMessageBox.confirm(`确定删除组织“${row.name}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteOrgUnit(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      await loadTree();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch {
    // ignore
  }
};

const unitTypeLabel = (type: number) => {
  return unitTypeOptions.find(x => x.value === type)?.label ?? "-";
};

watch(
  () => createForm.parentId,
  () => {
    syncCreateUnitType();
  }
);

onMounted(loadTree);
</script>

<template>
  <div class="page">
    <el-card>
      <template #header>
        <div class="flex items-center justify-between">
          <span>组织架构管理</span>
          <el-button type="primary" v-perms="'btn:org-unit:create'" @click="openCreateDialog()">
            新增组织
          </el-button>
        </div>
      </template>

      <el-table
        v-loading="loading"
        :data="treeData"
        row-key="id"
        default-expand-all
        :tree-props="{ children: 'children' }"
      >
        <el-table-column prop="name" label="组织名称" min-width="220" />
        <el-table-column prop="code" label="编码" min-width="120" />
        <el-table-column label="类型" width="120">
          <template #default="{ row }">
            {{ unitTypeLabel(row.unitType) }}
          </template>
        </el-table-column>
        <el-table-column prop="sort" label="排序" width="90" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'">
              {{ row.isActive ? "启用" : "停用" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="260" fixed="right">
          <template #default="{ row }">
            <el-button
              type="primary"
              link
              v-perms="'btn:org-unit:create'"
              :disabled="!canCreateChild(row)"
              @click="openCreateDialog(row.id)"
            >
              新增下级
            </el-button>
            <el-button type="primary" link v-perms="'btn:org-unit:update'" @click="openEditDialog(row)">
              编辑
            </el-button>
            <el-button type="danger" link v-perms="'btn:org-unit:delete'" @click="handleDelete(row)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="createDialogVisible" title="新增组织" width="520">
      <el-form label-width="90px">
        <el-form-item label="上级组织">
          <el-select v-model="createForm.parentId" class="w-full">
            <el-option v-if="!rootOrgUnit" label="无（顶级）" :value="null" />
            <el-option
              v-for="node in parentOptions"
              :key="node.id"
              :label="`${'　'.repeat(node.depth)}${node.name}`"
              :value="node.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="组织类型">
          <el-select v-model="createForm.unitType" class="w-full">
            <el-option
              v-for="item in createUnitTypeOptions"
              :key="item.value"
              :label="item.label"
              :value="item.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="组织编码" required>
          <el-input v-model="createForm.code" maxlength="64" />
        </el-form-item>
        <el-form-item label="组织名称" required>
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
          type="primary"
          v-perms="'btn:org-unit:create'"
          :disabled="createUnitTypeOptions.length === 0"
          @click="handleCreate"
        >
          创建
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editDialogVisible" title="编辑组织" width="520">
      <el-form label-width="90px">
        <el-form-item label="组织编码" required>
          <el-input v-model="editForm.code" maxlength="64" />
        </el-form-item>
        <el-form-item label="组织名称" required>
          <el-input v-model="editForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="排序">
          <el-input-number v-model="editForm.sort" :min="0" class="w-full" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="editForm.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" v-perms="'btn:org-unit:update'" @click="handleUpdate">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
}
</style>
