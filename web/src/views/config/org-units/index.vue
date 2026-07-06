<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import { getOrgUnitTree, updateOrgUnit, type OrgUnit } from "@/api/org-unit";
import { hasPerms } from "@/utils/auth";

defineOptions({
  name: "OrgUnitsConfig"
});

const loading = ref(false);
const treeData = ref<OrgUnit[]>([]);
const editDialogVisible = ref(false);

const editForm = reactive({
  id: 0,
  code: "",
  name: "",
  sort: 0,
  isActive: true
});

const canUpdate = computed(() => hasPerms("btn:org-unit:update"));
const rootOrgUnit = computed(() => treeData.value[0] ?? null);

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

const openEditDialog = (row: OrgUnit) => {
  editForm.id = row.id;
  editForm.code = row.code;
  editForm.name = row.name;
  editForm.sort = row.sort;
  editForm.isActive = row.isActive;
  editDialogVisible.value = true;
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

onMounted(loadTree);
</script>

<template>
  <div class="page">
    <el-card>
      <template #header>
        <div class="flex items-center justify-between">
          <div>
            <div class="page-title">组织管理</div>
            <div class="page-subtitle">
              系统固定为单组织模式，仅维护根组织信息
            </div>
          </div>
        </div>
      </template>

      <el-alert
        type="info"
        :closable="false"
        show-icon
        title="单组织系统规则"
        description="系统不支持新增下级组织、切换主组织或删除组织节点。"
        class="mb-4"
      />

      <el-table
        v-loading="loading"
        :data="rootOrgUnit ? [rootOrgUnit] : []"
        row-key="id"
        stripe
      >
        <el-table-column prop="name" label="组织名称" min-width="min(220px, calc(100vw - 32px))" />
        <el-table-column prop="code" label="编码" min-width="min(120px, calc(100vw - 32px))" />
        <el-table-column label="类型" width="min(120px, calc(100vw - 32px))">
          <template #default> 公司 </template>
        </el-table-column>
        <el-table-column prop="sort" label="排序" width="90" />
        <el-table-column label="状态" width="90">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'">
              {{ row.isActive ? "启用" : "停用" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column
          v-if="canUpdate"
          label="操作"
          width="min(140px, calc(100vw - 32px))"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button type="primary" link @click="openEditDialog(row)">
              编辑
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <el-empty
        v-if="!loading && !rootOrgUnit"
        description="未找到根组织数据"
      />
    </el-card>

    <el-dialog v-model="editDialogVisible" title="编辑组织" width="min(480px, calc(100vw - 32px))">
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
        <el-button
          v-perms="'btn:org-unit:update'"
          type="primary"
          @click="handleUpdate"
        >
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 0;
}

.mb-4 {
  margin-bottom: 16px;
}
</style>
