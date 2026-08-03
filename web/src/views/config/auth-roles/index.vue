<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  createAuthRole,
  deleteAuthRole,
  getAuthRoleList,
  updateAuthRole,
  type AuthRole,
  type CreateAuthRoleRequest,
  type UpdateAuthRoleRequest
} from "@/api/auth-role";
import {
  getAuthPermissionList,
  type AuthPermission
} from "@/api/auth-permission";
import { getOrgUnitFlat, type OrgUnit } from "@/api/org-unit";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import RoleFormDialog from "./components/RoleFormDialog.vue";
import { isProtectedBuiltInRole } from "./roleProtection";
import type { RoleFormModel, ScopeType } from "./roleForm.types";
import { normalizeScopeOrgUnitIds, validateScopeOrgUnitIds } from "./roleScope";

defineOptions({
  name: "AuthRolesConfig"
});

const loading = ref(false);
const submitLoading = ref(false);
const roles = ref<AuthRole[]>([]);
const permissions = ref<AuthPermission[]>([]);
const orgUnits = ref<OrgUnit[]>([]);
const keyword = ref("");

const createDialogVisible = ref(false);
const editDialogVisible = ref(false);

const scopeTypeOptions = [
  { label: "仅本人", value: 0 as ScopeType },
  { label: "单个组织", value: 1 as ScopeType },
  { label: "组织及子树", value: 2 as ScopeType },
  { label: "自定义组织", value: 3 as ScopeType },
  { label: "全部数据", value: 4 as ScopeType }
];

const createForm = reactive<RoleFormModel>({
  id: 0,
  code: "",
  name: "",
  description: "",
  isBuiltIn: false,
  isActive: true,
  permissionCodes: [],
  scopeType: 2,
  scopeOrgUnitIds: []
});

const editForm = reactive<RoleFormModel>({
  id: 0,
  code: "",
  name: "",
  description: "",
  isBuiltIn: false,
  isActive: true,
  permissionCodes: [],
  scopeType: 2,
  scopeOrgUnitIds: []
});

const orgUnitMap = computed(() => {
  return new Map(orgUnits.value.map(item => [item.id, item]));
});

const orgUnitOptions = computed(() => {
  return orgUnits.value.map(item => ({
    value: item.id,
    label: `${"　".repeat(Math.max(item.depth, 0))}${item.name} (${item.code})`,
    disabled: !item.isActive
  }));
});

const ensureScopeNodeSelection = (form: RoleFormModel) => {
  if (form.scopeType === 0 || form.scopeType === 4) {
    form.scopeOrgUnitIds = [];
    return;
  }

  if (
    (form.scopeType === 1 || form.scopeType === 2) &&
    form.scopeOrgUnitIds.length > 1
  ) {
    form.scopeOrgUnitIds = [form.scopeOrgUnitIds[0]];
  }
};

const normalizeStringList = (values: string[]) => {
  return [...new Set(values.map(item => item.trim()).filter(item => !!item))];
};

const normalizeNumberList = (values: number[]) => {
  return [
    ...new Set(values.filter(item => Number.isInteger(item) && item > 0))
  ];
};

const getDefaultScopeOrgId = () => {
  const root = orgUnits.value.find(
    item => item.unitType === 0 && item.parentId == null && item.isActive
  );
  if (root) return root.id;

  const firstActive = orgUnits.value.find(item => item.isActive);
  return firstActive?.id;
};

const resolveScopeOrgUnitIds = (
  scopeType: ScopeType,
  orgUnitIds?: number[]
) => {
  const normalized = normalizeScopeOrgUnitIds(scopeType, orgUnitIds ?? []);
  if ((scopeType === 1 || scopeType === 2) && normalized.length === 0) {
    const defaultOrgId = getDefaultScopeOrgId();
    return defaultOrgId ? [defaultOrgId] : [];
  }

  if ((scopeType === 1 || scopeType === 2) && normalized.length > 1) {
    return normalized.slice(0, 1);
  }

  if (scopeType === 0 || scopeType === 4) {
    return [];
  }

  return normalized;
};

const normalizeScopeType = (scopeType?: number) => {
  const normalized = scopeType ?? 2;
  return normalized === 0 ||
    normalized === 1 ||
    normalized === 2 ||
    normalized === 3 ||
    normalized === 4
    ? (normalized as ScopeType)
    : (2 as ScopeType);
};

const loadRoles = async () => {
  loading.value = true;
  try {
    const res = await getAuthRoleList({
      keyword: keyword.value.trim() || undefined
    });
    if (res.code === 0) {
      roles.value = res.data ?? [];
    } else {
      ElMessage.error(res.message || "加载角色失败");
    }
  } catch {
    ElMessage.error("加载角色失败");
  } finally {
    loading.value = false;
  }
};

const loadPermissions = async () => {
  try {
    const res = await getAuthPermissionList();
    if (res.code === 0) {
      permissions.value = res.data ?? [];
    } else {
      ElMessage.error(res.message || "加载权限字典失败");
    }
  } catch {
    ElMessage.error("加载权限字典失败");
  }
};

const loadOrgUnits = async () => {
  try {
    const res = await getOrgUnitFlat();
    if (res.code === 0) {
      orgUnits.value = (res.data ?? []).sort((a, b) => {
        if (a.depth !== b.depth) return a.depth - b.depth;
        if (a.sort !== b.sort) return a.sort - b.sort;
        return a.id - b.id;
      });
    } else {
      ElMessage.error(res.message || "加载组织失败");
    }
  } catch {
    ElMessage.error("加载组织失败");
  }
};

const initPage = async () => {
  loading.value = true;
  await Promise.all([loadPermissions(), loadOrgUnits(), loadRoles()]);
  loading.value = false;
};

const resetCreateForm = () => {
  const defaultOrgId = getDefaultScopeOrgId();
  createForm.id = 0;
  createForm.code = "";
  createForm.name = "";
  createForm.description = "";
  createForm.isBuiltIn = false;
  createForm.isActive = true;
  createForm.permissionCodes = [];
  createForm.scopeType = 2;
  createForm.scopeOrgUnitIds = defaultOrgId ? [defaultOrgId] : [];
};

const applyRoleToEditForm = (role: AuthRole) => {
  const specScope = role.dataScopes.find(item => item.resource === "spec");
  const normalizedScopeType = normalizeScopeType(specScope?.scopeType);
  editForm.id = role.id;
  editForm.code = role.code;
  editForm.name = role.name;
  editForm.description = role.description ?? "";
  editForm.isBuiltIn = role.isBuiltIn;
  editForm.isActive = role.isActive;
  editForm.permissionCodes = [...(role.permissionCodes ?? [])];
  editForm.scopeType = normalizedScopeType;
  editForm.scopeOrgUnitIds = resolveScopeOrgUnitIds(
    normalizedScopeType,
    specScope?.orgUnitIds
  );
  ensureScopeNodeSelection(editForm);
};

const openCreateDialog = () => {
  resetCreateForm();
  createDialogVisible.value = true;
};

const openEditDialog = (role: AuthRole) => {
  applyRoleToEditForm(role);
  editDialogVisible.value = true;
};

const updateCreateForm = (value: RoleFormModel) => {
  Object.assign(createForm, value);
};

const updateEditForm = (value: RoleFormModel) => {
  Object.assign(editForm, value);
};

const validateRoleForm = (form: RoleFormModel, isCreate: boolean) => {
  if (isCreate) {
    const code = form.code.trim().toLowerCase();
    if (!code) return "角色编码不能为空";
    if (!/^[a-z0-9._-]{2,64}$/.test(code)) {
      return "角色编码仅支持小写字母、数字、点、下划线、中划线，长度2-64";
    }
  }

  if (!form.name.trim()) {
    return "角色名称不能为空";
  }

  const scopeError = validateScopeOrgUnitIds(
    form.scopeType,
    form.scopeOrgUnitIds
  );
  if (scopeError) return scopeError;

  return null;
};

const buildDataScopes = (form: RoleFormModel) => {
  const orgUnitIds = normalizeNumberList(form.scopeOrgUnitIds);
  if (form.scopeType === 1 || form.scopeType === 2) {
    return [
      {
        resource: "spec",
        scopeType: form.scopeType,
        orgUnitIds: orgUnitIds.slice(0, 1)
      }
    ];
  }
  if (form.scopeType === 3) {
    return [
      {
        resource: "spec",
        scopeType: form.scopeType,
        orgUnitIds
      }
    ];
  }
  return [{ resource: "spec", scopeType: form.scopeType, orgUnitIds: [] }];
};

const handleCreate = async () => {
  const error = validateRoleForm(createForm, true);
  if (error) {
    ElMessage.warning(error);
    return;
  }

  const payload: CreateAuthRoleRequest = {
    code: createForm.code.trim().toLowerCase(),
    name: createForm.name.trim(),
    description: createForm.description.trim(),
    isActive: createForm.isActive,
    permissionCodes: normalizeStringList(createForm.permissionCodes),
    dataScopes: buildDataScopes(createForm)
  };

  submitLoading.value = true;
  try {
    const res = await createAuthRole(payload);
    if (res.code === 0) {
      ElMessage.success("创建角色成功");
      createDialogVisible.value = false;
      await loadRoles();
    } else {
      ElMessage.error(res.message || "创建角色失败");
    }
  } catch {
    ElMessage.error("创建角色失败");
  } finally {
    submitLoading.value = false;
  }
};

const handleUpdate = async () => {
  if (isProtectedBuiltInRole(editForm)) {
    ElMessage.warning("内置管理员角色只读，不可保存");
    return;
  }

  const error = validateRoleForm(editForm, false);
  if (error) {
    ElMessage.warning(error);
    return;
  }

  const payload: UpdateAuthRoleRequest = {
    name: editForm.name.trim(),
    description: editForm.description.trim(),
    isActive: editForm.isActive,
    permissionCodes: normalizeStringList(editForm.permissionCodes),
    dataScopes: buildDataScopes(editForm)
  };

  submitLoading.value = true;
  try {
    const res = await updateAuthRole(editForm.id, payload);
    if (res.code === 0) {
      ElMessage.success("更新角色成功");
      editDialogVisible.value = false;
      await loadRoles();
    } else {
      ElMessage.error(res.message || "更新角色失败");
    }
  } catch {
    ElMessage.error("更新角色失败");
  } finally {
    submitLoading.value = false;
  }
};

const handleDelete = async (role: AuthRole) => {
  try {
    await ElMessageBox.confirm(`确定删除角色“${role.name}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteAuthRole(role.id);
    if (res.code === 0) {
      ElMessage.success("删除角色成功");
      await loadRoles();
    } else {
      ElMessage.error(res.message || "删除角色失败");
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除角色失败"));
  }
};

const scopeTypeLabel = (scopeType: number) => {
  return (
    scopeTypeOptions.find(item => item.value === scopeType)?.label ?? "未配置"
  );
};

const formatScopeSummary = (role: AuthRole) => {
  const scope = role.dataScopes.find(item => item.resource === "spec");
  if (!scope) return "未配置";

  const normalizedScopeType = normalizeScopeType(scope.scopeType);
  const label = scopeTypeLabel(normalizedScopeType);
  if (normalizedScopeType === 1 || normalizedScopeType === 2) {
    const scopeOrgIds = resolveScopeOrgUnitIds(
      normalizedScopeType,
      scope.orgUnitIds
    );
    const org = scopeOrgIds[0]
      ? orgUnitMap.value.get(scopeOrgIds[0])
      : undefined;
    return `${label}${org ? `：${org.name}` : ""}`;
  }
  if (normalizedScopeType === 3) {
    const names = resolveScopeOrgUnitIds(normalizedScopeType, scope.orgUnitIds)
      .map(id => orgUnitMap.value.get(id)?.name)
      .filter(Boolean);
    if (names.length === 0) return label;
    if (names.length <= 2) return `${label}：${names.join("、")}`;
    return `${label}：${names.slice(0, 2).join("、")} 等 ${names.length} 个`;
  }
  return label;
};

const handleSearch = () => {
  loadRoles();
};

const handleReset = () => {
  keyword.value = "";
  loadRoles();
};

onMounted(initPage);
</script>

<template>
  <div class="page config-page">
    <el-card class="full-height-table-wrapper">
      <template #header>
        <div class="list-card-toolbar">
          <div class="list-card-toolbar__right">
            <el-form :inline="true" class="filter-form">
              <el-form-item label="关键词">
                <el-input
                  v-model="keyword"
                  placeholder="角色编码/角色名称"
                  clearable
                  @keyup.enter="handleSearch"
                />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" @click="handleSearch">搜索</el-button>
                <el-button @click="handleReset">重置</el-button>
              </el-form-item>
            </el-form>
            <el-button
              v-perms="'btn:auth-role:create'"
              type="primary"
              @click="openCreateDialog"
            >
              新增角色
            </el-button>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="roles" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="code" label="角色编码" min-width="160" />
        <el-table-column prop="name" label="角色名称" min-width="140" />
        <el-table-column label="内置角色" width="110">
          <template #default="{ row }">
            <el-tag :type="row.isBuiltIn ? 'warning' : 'info'">
              {{ row.isBuiltIn ? "是" : "否" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="100">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'">
              {{ row.isActive ? "启用" : "停用" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="权限数" width="100">
          <template #default="{ row }">
            {{ row.permissionCodes?.length ?? 0 }}
          </template>
        </el-table-column>
        <el-table-column label="验收规格数据范围" min-width="280">
          <template #default="{ row }">
            {{ formatScopeSummary(row) }}
          </template>
        </el-table-column>
        <el-table-column
          prop="description"
          label="描述"
          min-width="220"
          show-overflow-tooltip
        />
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button
              v-perms="'btn:auth-role:update'"
              type="primary"
              link
              @click="openEditDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-perms="'btn:auth-role:delete'"
              type="danger"
              link
              :disabled="row.isBuiltIn"
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <RoleFormDialog
      v-model:visible="createDialogVisible"
      mode="create"
      :model-value="createForm"
      :permissions="permissions"
      :scope-type-options="scopeTypeOptions"
      :org-unit-options="orgUnitOptions"
      :submitting="submitLoading"
      @update:model-value="updateCreateForm"
      @submit="handleCreate"
    />

    <RoleFormDialog
      v-model:visible="editDialogVisible"
      mode="edit"
      :model-value="editForm"
      :permissions="permissions"
      :scope-type-options="scopeTypeOptions"
      :org-unit-options="orgUnitOptions"
      :submitting="submitLoading"
      @update:model-value="updateEditForm"
      @submit="handleUpdate"
    />
  </div>
</template>

<style scoped>
.page {
  padding: 0;
}
</style>
