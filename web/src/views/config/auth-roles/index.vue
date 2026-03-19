<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
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
import { getAuthPermissionList, type AuthPermission } from "@/api/auth-permission";
import { getOrgUnitFlat, type OrgUnit } from "@/api/org-unit";

defineOptions({
  name: "AuthRolesConfig"
});

type ScopeType = 0 | 1 | 2 | 3 | 4;

interface RoleFormModel {
  id: number;
  code: string;
  name: string;
  description: string;
  isBuiltIn: boolean;
  isActive: boolean;
  permissionCodes: string[];
  scopeType: ScopeType;
  scopeOrgUnitIds: number[];
}

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
  { label: "自定义多组织", value: 3 as ScopeType },
  { label: "全部数据", value: 4 as ScopeType }
];

const permissionTypeLabels: Record<number, string> = {
  0: "页面权限",
  1: "按钮权限",
  2: "接口权限"
};

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

const permissionGroups = computed(() => {
  const grouped: Record<number, AuthPermission[]> = {
    0: [],
    1: [],
    2: []
  };

  permissions.value.forEach(item => {
    if (grouped[item.permissionType]) {
      grouped[item.permissionType].push(item);
    }
  });

  return Object.keys(grouped)
    .map(key => Number(key))
    .map(type => ({
      type,
      label: permissionTypeLabels[type] ?? "其他",
      items: grouped[type].sort((a, b) => a.code.localeCompare(b.code))
    }))
    .filter(group => group.items.length > 0);
});

const permissionCodeMap = computed(() => {
  return new Map(permissions.value.map(item => [item.code, item]));
});

const createSingleScopeOrgId = computed<number | null>({
  get: () => createForm.scopeOrgUnitIds[0] ?? null,
  set: value => {
    createForm.scopeOrgUnitIds = value ? [value] : [];
  }
});

const editSingleScopeOrgId = computed<number | null>({
  get: () => editForm.scopeOrgUnitIds[0] ?? null,
  set: value => {
    editForm.scopeOrgUnitIds = value ? [value] : [];
  }
});

const ensureScopeNodeSelection = (form: RoleFormModel) => {
  if (form.scopeType === 0 || form.scopeType === 4) {
    form.scopeOrgUnitIds = [];
    return;
  }

  if ((form.scopeType === 1 || form.scopeType === 2) && form.scopeOrgUnitIds.length > 1) {
    form.scopeOrgUnitIds = [form.scopeOrgUnitIds[0]];
  }
};

watch(
  () => createForm.scopeType,
  () => ensureScopeNodeSelection(createForm)
);

watch(
  () => editForm.scopeType,
  () => ensureScopeNodeSelection(editForm)
);

const normalizeStringList = (values: string[]) => {
  return [...new Set(values.map(item => item.trim()).filter(item => !!item))];
};

const normalizeNumberList = (values: number[]) => {
  return [...new Set(values.filter(item => Number.isInteger(item) && item > 0))];
};

const getPermissionCodesByType = (permissionType: number) => {
  return permissions.value
    .filter(item => item.permissionType === permissionType)
    .map(item => item.code);
};

const mergePermissionCodes = (form: RoleFormModel, permissionCodes: string[]) => {
  form.permissionCodes = normalizeStringList([...form.permissionCodes, ...permissionCodes]);
};

const removePermissionCodes = (form: RoleFormModel, permissionCodes: string[]) => {
  const removeSet = new Set(permissionCodes);
  form.permissionCodes = form.permissionCodes.filter(code => !removeSet.has(code));
};

const selectAllPermissions = (form: RoleFormModel) => {
  form.permissionCodes = normalizeStringList(permissions.value.map(item => item.code));
};

const clearAllPermissions = (form: RoleFormModel) => {
  form.permissionCodes = [];
};

const selectPermissionType = (form: RoleFormModel, permissionType: number) => {
  mergePermissionCodes(form, getPermissionCodesByType(permissionType));
};

const clearPermissionType = (form: RoleFormModel, permissionType: number) => {
  removePermissionCodes(form, getPermissionCodesByType(permissionType));
};

const getSelectedPermissionItems = (form: RoleFormModel) => {
  return form.permissionCodes
    .map(code => permissionCodeMap.value.get(code))
    .filter(item => !!item) as AuthPermission[];
};

const createSelectedPermissionItems = computed(() => getSelectedPermissionItems(createForm));
const editSelectedPermissionItems = computed(() => getSelectedPermissionItems(editForm));

const permissionTagType = (permissionType: number) => {
  if (permissionType === 0) return "success";
  if (permissionType === 1) return "warning";
  return "info";
};

const getDefaultScopeOrgId = () => {
  const root = orgUnits.value.find(item => item.unitType === 0 && item.parentId == null && item.isActive);
  if (root) return root.id;

  const firstActive = orgUnits.value.find(item => item.isActive);
  return firstActive?.id;
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
  editForm.id = role.id;
  editForm.code = role.code;
  editForm.name = role.name;
  editForm.description = role.description ?? "";
  editForm.isBuiltIn = role.isBuiltIn;
  editForm.isActive = role.isActive;
  editForm.permissionCodes = [...(role.permissionCodes ?? [])];
  editForm.scopeType = (specScope?.scopeType ?? 2) as ScopeType;
  editForm.scopeOrgUnitIds = [...(specScope?.orgUnitIds ?? [])];
  ensureScopeNodeSelection(editForm);
};

const openCreateDialog = () => {
  resetCreateForm();
  createDialogVisible.value = true;
};

const openEditDialog = (role: AuthRole) => {
  if (role.isBuiltIn) {
    ElMessage.warning("内置角色不允许编辑");
    return;
  }
  applyRoleToEditForm(role);
  editDialogVisible.value = true;
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

  const nodeIds = normalizeNumberList(form.scopeOrgUnitIds);
  if ((form.scopeType === 1 || form.scopeType === 2) && nodeIds.length !== 1) {
    return "当前数据范围必须选择一个组织节点";
  }
  if (form.scopeType === 3 && nodeIds.length === 0) {
    return "自定义多组织至少选择一个组织节点";
  }

  return null;
};

const buildDataScopes = (form: RoleFormModel) => {
  const orgUnitIds = normalizeNumberList(form.scopeOrgUnitIds);
  if (form.scopeType === 1 || form.scopeType === 2) {
    return [{ resource: "spec", scopeType: form.scopeType, orgUnitIds: orgUnitIds.slice(0, 1) }];
  }
  if (form.scopeType === 3) {
    return [{ resource: "spec", scopeType: form.scopeType, orgUnitIds }];
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
  } catch {
    // 取消删除
  }
};

const scopeTypeLabel = (scopeType: number) => {
  return scopeTypeOptions.find(item => item.value === scopeType)?.label ?? "未配置";
};

const formatScopeSummary = (role: AuthRole) => {
  const scope = role.dataScopes.find(item => item.resource === "spec");
  if (!scope) return "未配置";

  const label = scopeTypeLabel(scope.scopeType);
  if (scope.scopeType === 1 || scope.scopeType === 2) {
    const org = scope.orgUnitIds[0] ? orgUnitMap.value.get(scope.orgUnitIds[0]) : undefined;
    return `${label}${org ? `：${org.name}` : ""}`;
  }
  if (scope.scopeType === 3) {
    const names = scope.orgUnitIds
      .map(id => orgUnitMap.value.get(id)?.name)
      .filter(name => !!name) as string[];
    if (names.length === 0) return label;
    return `${label}：${names.join("、")}`;
  }
  return label;
};

const needsSingleOrg = (scopeType: ScopeType) => scopeType === 1 || scopeType === 2;
const needsMultiOrg = (scopeType: ScopeType) => scopeType === 3;

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
  <div class="page">
    <el-card class="mb-4">
      <el-form :inline="true">
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
    </el-card>

    <el-card>
      <template #header>
        <div class="flex items-center justify-between">
          <span>角色管理</span>
          <el-button type="primary" v-perms="'btn:auth-role:create'" @click="openCreateDialog">
            新增角色
          </el-button>
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
        <el-table-column prop="description" label="描述" min-width="220" show-overflow-tooltip />
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button
              type="primary"
              link
              v-perms="'btn:auth-role:update'"
              :disabled="row.isBuiltIn"
              @click="openEditDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              type="danger"
              link
              v-perms="'btn:auth-role:delete'"
              :disabled="row.isBuiltIn"
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="createDialogVisible" title="新增角色" width="760">
      <el-form label-width="110px">
        <el-form-item label="角色编码" required>
          <el-input v-model="createForm.code" maxlength="64" placeholder="例：qa_leader" />
        </el-form-item>
        <el-form-item label="角色名称" required>
          <el-input v-model="createForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="角色描述">
          <el-input
            v-model="createForm.description"
            type="textarea"
            :rows="2"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="createForm.isActive" />
        </el-form-item>
        <el-form-item>
          <template #label>
            权限配置
            <span class="permission-count">({{ createForm.permissionCodes.length }})</span>
          </template>
          <div class="permission-panel w-full">
            <div class="permission-actions">
              <el-button text type="primary" @click="selectPermissionType(createForm, 0)">全选页面</el-button>
              <el-button text type="primary" @click="selectPermissionType(createForm, 1)">全选按钮</el-button>
              <el-button text type="primary" @click="selectPermissionType(createForm, 2)">全选接口</el-button>
              <el-button text type="warning" @click="clearPermissionType(createForm, 0)">清空页面</el-button>
              <el-button text type="warning" @click="clearPermissionType(createForm, 1)">清空按钮</el-button>
              <el-button text type="warning" @click="clearPermissionType(createForm, 2)">清空接口</el-button>
              <el-button text @click="selectAllPermissions(createForm)">全选全部</el-button>
              <el-button text type="danger" @click="clearAllPermissions(createForm)">清空全部</el-button>
            </div>
            <el-select
              v-model="createForm.permissionCodes"
              multiple
              filterable
              collapse-tags
              collapse-tags-tooltip
              class="w-full"
            >
              <el-option-group
                v-for="group in permissionGroups"
                :key="`create-perm-group-${group.type}`"
                :label="group.label"
              >
                <el-option
                  v-for="permission in group.items"
                  :key="`create-perm-${permission.code}`"
                  :label="`${permission.code} - ${permission.name}`"
                  :value="permission.code"
                />
              </el-option-group>
            </el-select>
            <div v-if="createSelectedPermissionItems.length > 0" class="selected-permission-list">
              <el-tag
                v-for="permission in createSelectedPermissionItems"
                :key="`create-selected-${permission.code}`"
                :type="permissionTagType(permission.permissionType)"
                size="small"
                class="mr-1 mb-1"
              >
                {{ permission.code }}
              </el-tag>
            </div>
            <div v-else class="selected-permission-empty">未选择权限</div>
          </div>
        </el-form-item>
        <el-form-item label="验收规格范围" required>
          <el-select v-model="createForm.scopeType" class="w-full">
            <el-option
              v-for="option in scopeTypeOptions"
              :key="`create-scope-type-${option.value}`"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="needsSingleOrg(createForm.scopeType)" label="组织节点" required>
          <el-select v-model="createSingleScopeOrgId" clearable filterable class="w-full">
            <el-option
              v-for="option in orgUnitOptions"
              :key="`create-scope-org-${option.value}`"
              :label="option.label"
              :value="option.value"
              :disabled="option.disabled"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="needsMultiOrg(createForm.scopeType)" label="组织节点" required>
          <el-select v-model="createForm.scopeOrgUnitIds" multiple filterable class="w-full">
            <el-option
              v-for="option in orgUnitOptions"
              :key="`create-scope-org-${option.value}`"
              :label="option.label"
              :value="option.value"
              :disabled="option.disabled"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" v-perms="'btn:auth-role:create'" @click="handleCreate">
          创建
        </el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editDialogVisible" title="编辑角色" width="760">
      <el-form label-width="110px">
        <el-form-item label="角色编码">
          <el-input :model-value="editForm.code" disabled />
        </el-form-item>
        <el-form-item label="角色名称" required>
          <el-input v-model="editForm.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="角色描述">
          <el-input
            v-model="editForm.description"
            type="textarea"
            :rows="2"
            maxlength="500"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-switch v-model="editForm.isActive" :disabled="editForm.isBuiltIn" />
        </el-form-item>
        <el-form-item>
          <template #label>
            权限配置
            <span class="permission-count">({{ editForm.permissionCodes.length }})</span>
          </template>
          <div class="permission-panel w-full">
            <div class="permission-actions">
              <el-button text type="primary" @click="selectPermissionType(editForm, 0)">全选页面</el-button>
              <el-button text type="primary" @click="selectPermissionType(editForm, 1)">全选按钮</el-button>
              <el-button text type="primary" @click="selectPermissionType(editForm, 2)">全选接口</el-button>
              <el-button text type="warning" @click="clearPermissionType(editForm, 0)">清空页面</el-button>
              <el-button text type="warning" @click="clearPermissionType(editForm, 1)">清空按钮</el-button>
              <el-button text type="warning" @click="clearPermissionType(editForm, 2)">清空接口</el-button>
              <el-button text @click="selectAllPermissions(editForm)">全选全部</el-button>
              <el-button text type="danger" @click="clearAllPermissions(editForm)">清空全部</el-button>
            </div>
            <el-select
              v-model="editForm.permissionCodes"
              multiple
              filterable
              collapse-tags
              collapse-tags-tooltip
              class="w-full"
            >
              <el-option-group
                v-for="group in permissionGroups"
                :key="`edit-perm-group-${group.type}`"
                :label="group.label"
              >
                <el-option
                  v-for="permission in group.items"
                  :key="`edit-perm-${permission.code}`"
                  :label="`${permission.code} - ${permission.name}`"
                  :value="permission.code"
                />
              </el-option-group>
            </el-select>
            <div v-if="editSelectedPermissionItems.length > 0" class="selected-permission-list">
              <el-tag
                v-for="permission in editSelectedPermissionItems"
                :key="`edit-selected-${permission.code}`"
                :type="permissionTagType(permission.permissionType)"
                size="small"
                class="mr-1 mb-1"
              >
                {{ permission.code }}
              </el-tag>
            </div>
            <div v-else class="selected-permission-empty">未选择权限</div>
          </div>
        </el-form-item>
        <el-form-item label="验收规格范围" required>
          <el-select v-model="editForm.scopeType" class="w-full">
            <el-option
              v-for="option in scopeTypeOptions"
              :key="`edit-scope-type-${option.value}`"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="needsSingleOrg(editForm.scopeType)" label="组织节点" required>
          <el-select v-model="editSingleScopeOrgId" clearable filterable class="w-full">
            <el-option
              v-for="option in orgUnitOptions"
              :key="`edit-scope-org-${option.value}`"
              :label="option.label"
              :value="option.value"
              :disabled="option.disabled"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="needsMultiOrg(editForm.scopeType)" label="组织节点" required>
          <el-select v-model="editForm.scopeOrgUnitIds" multiple filterable class="w-full">
            <el-option
              v-for="option in orgUnitOptions"
              :key="`edit-scope-org-${option.value}`"
              :label="option.label"
              :value="option.value"
              :disabled="option.disabled"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="submitLoading" v-perms="'btn:auth-role:update'" @click="handleUpdate">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
}

.permission-count {
  margin-left: 4px;
  color: #909399;
  font-size: 12px;
}

.permission-panel {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.permission-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 8px;
}

.selected-permission-list {
  max-height: 100px;
  overflow-y: auto;
  border: 1px solid var(--el-border-color-light);
  border-radius: 4px;
  padding: 8px;
}

.selected-permission-empty {
  font-size: 12px;
  color: #909399;
}
</style>
