<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import { storageLocal } from "@pureadmin/utils";
import { hasPerms, userKey, type DataInfo } from "@/utils/auth";
import {
  createSystemUser,
  deleteSystemUser,
  getSystemUserList,
  resetSystemUserPassword,
  updateSystemUser,
  updateSystemUserStatus,
  type CreateSystemUserRequest,
  type SystemUser,
  type UpdateSystemUserRequest
} from "@/api/system-user";
import { getAuthRoleList, type AuthRole } from "@/api/auth-role";
import { getOrgUnitFlat, type OrgUnit } from "@/api/org-unit";

defineOptions({
  name: "SystemUsersConfig"
});

type StatusFilter = "all" | "active" | "inactive";

const loading = ref(false);
const tableData = ref<SystemUser[]>([]);
const total = ref(0);
const roleOptions = ref<AuthRole[]>([]);
const orgUnitOptions = ref<OrgUnit[]>([]);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: "",
  status: "all" as StatusFilter
});

const createDialogVisible = ref(false);
const editDialogVisible = ref(false);
const resetPasswordDialogVisible = ref(false);

const createForm = reactive({
  username: "",
  password: "",
  nickname: "",
  avatar: "",
  roles: [] as string[],
  primaryOrgUnitId: null as number | null,
  orgUnitIds: [] as number[],
  isActive: true
});

const editForm = reactive({
  id: 0,
  username: "",
  nickname: "",
  avatar: "",
  roles: [] as string[],
  primaryOrgUnitId: null as number | null,
  orgUnitIds: [] as number[],
  isActive: true
});

const resetPasswordForm = reactive({
  userId: 0,
  username: "",
  newPassword: "",
  confirmPassword: ""
});

const currentUsername = computed(() => {
  const userInfo = storageLocal().getItem<DataInfo<number>>(userKey);
  return (userInfo?.username ?? "").trim();
});

const listRequestParams = computed(() => {
  const isActive =
    queryParams.status === "active"
      ? true
      : queryParams.status === "inactive"
        ? false
        : undefined;
  return {
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    keyword: queryParams.keyword.trim() || undefined,
    isActive
  };
});

const activeRoleOptions = computed(() => {
  return roleOptions.value.filter(item => item.isActive);
});

const roleNameMap = computed(() => {
  return new Map(roleOptions.value.map(item => [item.code, item.name]));
});

const activeOrgUnitOptions = computed(() => {
  return orgUnitOptions.value.filter(item => item.isActive);
});

const orgUnitMap = computed(() => {
  return new Map(orgUnitOptions.value.map(item => [item.id, item]));
});

const createPrimaryOrgOptions = computed(() => {
  return createForm.orgUnitIds
    .map(id => orgUnitMap.value.get(id))
    .filter(item => !!item) as OrgUnit[];
});

const editPrimaryOrgOptions = computed(() => {
  return editForm.orgUnitIds
    .map(id => orgUnitMap.value.get(id))
    .filter(item => !!item) as OrgUnit[];
});

const normalizeStringList = (values: string[]) => {
  return [...new Set(values.map(item => item.trim()).filter(item => !!item))];
};

const normalizeNumberList = (values: number[]) => {
  return [...new Set(values.filter(item => Number.isInteger(item) && item > 0))];
};

const isValidUsername = (username: string) => /^[A-Za-z0-9._-]{3,64}$/.test(username);

const getDefaultRoleCodes = () => {
  const preferred = activeRoleOptions.value.find(item => item.code === "common");
  if (preferred) return [preferred.code];
  const first = activeRoleOptions.value[0];
  return first ? [first.code] : [];
};

const getDefaultPrimaryOrgId = () => {
  const root = activeOrgUnitOptions.value.find(
    item => item.unitType === 0 && item.parentId == null
  );
  if (root) return root.id;

  const first = activeOrgUnitOptions.value[0];
  return first?.id;
};

const syncOrgSelection = (form: {
  orgUnitIds: number[];
  primaryOrgUnitId: number | null;
}) => {
  form.orgUnitIds = normalizeNumberList(form.orgUnitIds);
  if (form.primaryOrgUnitId && !form.orgUnitIds.includes(form.primaryOrgUnitId)) {
    form.primaryOrgUnitId = form.orgUnitIds[0] ?? null;
  }
  if (!form.primaryOrgUnitId && form.orgUnitIds.length > 0) {
    form.primaryOrgUnitId = form.orgUnitIds[0];
  }
};

const loadRoleOptions = async () => {
  const res = await getAuthRoleList();
  if (res.code === 0) {
    roleOptions.value = (res.data ?? []).sort((a, b) => a.code.localeCompare(b.code));
  } else {
    ElMessage.error(res.message || "加载角色列表失败");
  }
};

const loadOrgUnitOptions = async () => {
  const res = await getOrgUnitFlat();
  if (res.code === 0) {
    orgUnitOptions.value = (res.data ?? []).sort((a, b) => {
      if (a.depth !== b.depth) return a.depth - b.depth;
      if (a.sort !== b.sort) return a.sort - b.sort;
      return a.id - b.id;
    });
  } else {
    ElMessage.error(res.message || "加载组织列表失败");
  }
};

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getSystemUserList(listRequestParams.value);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message || "加载系统用户失败");
    }
  } catch {
    ElMessage.error("加载系统用户失败");
  } finally {
    loading.value = false;
  }
};

const initPage = async () => {
  try {
    await Promise.all([loadRoleOptions(), loadOrgUnitOptions()]);
  } catch {
    // 错误在各自方法内已提示
  }
  await loadData();
};

const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};

const handleReset = () => {
  queryParams.page = 1;
  queryParams.pageSize = 20;
  queryParams.keyword = "";
  queryParams.status = "all";
  loadData();
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};

const handleSizeChange = (size: number) => {
  queryParams.page = 1;
  queryParams.pageSize = size;
  loadData();
};

const openCreateDialog = () => {
  const defaultOrgId = getDefaultPrimaryOrgId();
  createForm.username = "";
  createForm.password = "";
  createForm.nickname = "";
  createForm.avatar = "";
  createForm.roles = getDefaultRoleCodes();
  createForm.primaryOrgUnitId = defaultOrgId ?? null;
  createForm.orgUnitIds = defaultOrgId ? [defaultOrgId] : [];
  createForm.isActive = true;
  createDialogVisible.value = true;
};

const openEditDialog = (row: SystemUser) => {
  const orgIds = row.orgUnits.map(item => item.orgUnitId);
  const primaryOrg = row.orgUnits.find(item => item.isPrimary);
  editForm.id = row.id;
  editForm.username = row.username;
  editForm.nickname = row.nickname;
  editForm.avatar = row.avatar ?? "";
  editForm.roles = [...(row.roles || [])];
  editForm.orgUnitIds = normalizeNumberList(orgIds);
  editForm.primaryOrgUnitId = primaryOrg?.orgUnitId ?? editForm.orgUnitIds[0] ?? null;
  editForm.isActive = row.isActive;
  syncOrgSelection(editForm);
  editDialogVisible.value = true;
};

const handleCreate = async () => {
  const username = createForm.username.trim();
  const password = createForm.password;
  const nickname = createForm.nickname.trim();
  const roles = normalizeStringList(createForm.roles);

  if (!isValidUsername(username)) {
    ElMessage.warning("用户名仅支持字母、数字、点、下划线、中划线，长度3-64");
    return;
  }
  if (!password || password.length < 8) {
    ElMessage.warning("密码长度至少8位");
    return;
  }
  if (!nickname) {
    ElMessage.warning("请输入昵称");
    return;
  }
  if (roles.length === 0) {
    ElMessage.warning("至少配置一个角色");
    return;
  }

  syncOrgSelection(createForm);
  if (!createForm.orgUnitIds.length || !createForm.primaryOrgUnitId) {
    ElMessage.warning("至少选择一个组织，并指定主组织");
    return;
  }

  const payload: CreateSystemUserRequest = {
    username,
    password,
    nickname,
    avatar: createForm.avatar.trim() || "",
    roles,
    primaryOrgUnitId: createForm.primaryOrgUnitId,
    orgUnitIds: createForm.orgUnitIds,
    isActive: createForm.isActive
  };

  try {
    const res = await createSystemUser(payload);
    if (res.code === 0) {
      ElMessage.success("创建用户成功");
      createDialogVisible.value = false;
      await loadData();
    } else {
      ElMessage.error(res.message || "创建用户失败");
    }
  } catch {
    ElMessage.error("创建用户失败");
  }
};

const handleUpdate = async () => {
  const nickname = editForm.nickname.trim();
  const roles = normalizeStringList(editForm.roles);
  if (!nickname) {
    ElMessage.warning("请输入昵称");
    return;
  }
  if (roles.length === 0) {
    ElMessage.warning("至少配置一个角色");
    return;
  }

  syncOrgSelection(editForm);
  if (!editForm.orgUnitIds.length || !editForm.primaryOrgUnitId) {
    ElMessage.warning("至少选择一个组织，并指定主组织");
    return;
  }

  const payload: UpdateSystemUserRequest = {
    nickname,
    avatar: editForm.avatar.trim() || "",
    roles,
    primaryOrgUnitId: editForm.primaryOrgUnitId,
    orgUnitIds: editForm.orgUnitIds,
    isActive: editForm.isActive
  };

  try {
    const res = await updateSystemUser(editForm.id, payload);
    if (res.code === 0) {
      ElMessage.success("更新用户成功");
      editDialogVisible.value = false;
      await loadData();
    } else {
      ElMessage.error(res.message || "更新用户失败");
    }
  } catch {
    ElMessage.error("更新用户失败");
  }
};

const openResetPasswordDialog = (row: SystemUser) => {
  resetPasswordForm.userId = row.id;
  resetPasswordForm.username = row.username;
  resetPasswordForm.newPassword = "";
  resetPasswordForm.confirmPassword = "";
  resetPasswordDialogVisible.value = true;
};

const handleResetPassword = async () => {
  const newPassword = resetPasswordForm.newPassword;
  const confirmPassword = resetPasswordForm.confirmPassword;
  if (!newPassword || newPassword.length < 8) {
    ElMessage.warning("新密码长度至少8位");
    return;
  }
  if (newPassword !== confirmPassword) {
    ElMessage.warning("两次输入的密码不一致");
    return;
  }

  try {
    const res = await resetSystemUserPassword(resetPasswordForm.userId, {
      newPassword
    });
    if (res.code === 0) {
      ElMessage.success("重置密码成功");
      resetPasswordDialogVisible.value = false;
    } else {
      ElMessage.error(res.message || "重置密码失败");
    }
  } catch {
    ElMessage.error("重置密码失败");
  }
};

const handleDelete = async (row: SystemUser) => {
  try {
    await ElMessageBox.confirm(`确定删除用户“${row.username}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });

    const res = await deleteSystemUser(row.id);
    if (res.code === 0) {
      ElMessage.success("删除用户成功");
      await loadData();
    } else {
      ElMessage.error(res.message || "删除用户失败");
    }
  } catch {
    // cancelled
  }
};

const updatingStatusIds = ref<number[]>([]);

const handleToggleStatus = async (row: SystemUser, value: boolean) => {
  if (updatingStatusIds.value.includes(row.id)) return;
  updatingStatusIds.value.push(row.id);

  try {
    const res = await updateSystemUserStatus(row.id, { isActive: value });
    if (res.code === 0) {
      ElMessage.success("状态更新成功");
      row.isActive = value;
    } else {
      ElMessage.error(res.message || "状态更新失败");
      row.isActive = !value;
    }
  } catch {
    ElMessage.error("状态更新失败");
    row.isActive = !value;
  } finally {
    updatingStatusIds.value = updatingStatusIds.value.filter(id => id !== row.id);
  }
};

const canDelete = (row: SystemUser) => {
  return row.username !== currentUsername.value;
};

const canDisable = (row: SystemUser) => {
  return !(row.username === currentUsername.value && row.isActive);
};

const formatDateTime = (value?: string | null) => {
  if (!value) return "-";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return "-";
  return date.toLocaleString();
};

const formatRoleLabel = (roleCode: string) => {
  const roleName = roleNameMap.value.get(roleCode);
  return roleName ? `${roleName} (${roleCode})` : roleCode;
};

const formatOrgLabel = (orgUnitId: number) => {
  const org = orgUnitMap.value.get(orgUnitId);
  if (!org) return `组织#${orgUnitId}`;
  return `${org.name} (${org.code})`;
};

const handleCreateOrgChange = (values: number[]) => {
  createForm.orgUnitIds = normalizeNumberList(values);
  syncOrgSelection(createForm);
};

const handleEditOrgChange = (values: number[]) => {
  editForm.orgUnitIds = normalizeNumberList(values);
  syncOrgSelection(editForm);
};

onMounted(initPage);
</script>

<template>
  <div class="page config-page">
    <div class="page-header">
      <div>
        <div class="page-title">系统用户管理</div>
        <div class="page-subtitle">管理登录账号、角色、组织分配与启用状态</div>
      </div>
    </div>

    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="用户名/昵称"
            clearable
            @keyup.enter="handleSearch"
          />
        </el-form-item>
        <el-form-item label="状态">
          <el-select
            v-model="queryParams.status"
            class="w-[180px]"
            popper-class="config-select-popper"
          >
            <el-option label="全部" value="all" />
            <el-option label="启用" value="active" />
            <el-option label="禁用" value="inactive" />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="handleSearch">搜索</el-button>
          <el-button @click="handleReset">重置</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>系统用户</span>
          <el-button type="primary" v-perms="'btn:system-user:create'" @click="openCreateDialog">
            新增用户
          </el-button>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="username" label="用户名" min-width="140" />
        <el-table-column prop="nickname" label="昵称" min-width="120" />
        <el-table-column label="角色" min-width="220">
          <template #default="{ row }">
            <el-tag
              v-for="role in row.roles"
              :key="`${row.id}-${role}`"
              type="success"
              class="mr-1 mb-1"
              size="small"
            >
              {{ formatRoleLabel(role) }}
            </el-tag>
            <span v-if="!row.roles?.length">-</span>
          </template>
        </el-table-column>
        <el-table-column label="组织" min-width="240">
          <template #default="{ row }">
            <el-tag
              v-for="org in row.orgUnits"
              :key="`${row.id}-${org.orgUnitId}`"
              :type="org.isPrimary ? 'warning' : 'info'"
              class="mr-1 mb-1"
              size="small"
            >
              {{ formatOrgLabel(org.orgUnitId) }}{{ org.isPrimary ? "（主）" : "" }}
            </el-tag>
            <span v-if="!row.orgUnits?.length">-</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="120">
          <template #default="{ row }">
            <el-switch
              :model-value="row.isActive"
              :loading="updatingStatusIds.includes(row.id)"
              :disabled="!canDisable(row) || !hasPerms('btn:system-user:update-status')"
              @update:model-value="value => handleToggleStatus(row, value === true)"
            />
          </template>
        </el-table-column>
        <el-table-column label="更新时间" width="180">
          <template #default="{ row }">
            {{ formatDateTime(row.updatedAt ?? row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="300" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link v-perms="'btn:system-user:update'" @click="openEditDialog(row)">
              编辑
            </el-button>
            <el-button type="warning" link v-perms="'btn:system-user:reset-password'" @click="openResetPasswordDialog(row)">
              重置密码
            </el-button>
            <el-button
              type="danger"
              link
              v-perms="'btn:system-user:delete'"
              :disabled="!canDelete(row)"
              @click="handleDelete(row)"
            >
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>

      <div class="mt-4 flex justify-end">
        <el-pagination
          v-model:current-page="queryParams.page"
          v-model:page-size="queryParams.pageSize"
          :page-sizes="[10, 20, 50, 100]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="handleSizeChange"
          @current-change="handlePageChange"
        />
      </div>
    </el-card>

    <el-dialog v-model="createDialogVisible" title="新增用户" width="660">
      <el-form label-width="110px">
        <el-form-item label="用户名" required>
          <el-input
            v-model="createForm.username"
            maxlength="64"
            placeholder="3-64位，支持字母/数字/._-"
          />
        </el-form-item>
        <el-form-item label="密码" required>
          <el-input
            v-model="createForm.password"
            type="password"
            show-password
            placeholder="至少8位"
          />
        </el-form-item>
        <el-form-item label="昵称" required>
          <el-input v-model="createForm.nickname" maxlength="100" />
        </el-form-item>
        <el-form-item label="头像">
          <el-input v-model="createForm.avatar" maxlength="500" />
        </el-form-item>
        <el-form-item label="角色" required>
          <el-select
            v-model="createForm.roles"
            multiple
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="role in activeRoleOptions"
              :key="`create-role-${role.code}`"
              :label="`${role.name} (${role.code})`"
              :value="role.code"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关联组织" required>
          <el-select
            v-model="createForm.orgUnitIds"
            multiple
            filterable
            class="w-full"
            popper-class="config-select-popper"
            @change="handleCreateOrgChange"
          >
            <el-option
              v-for="org in activeOrgUnitOptions"
              :key="`create-org-${org.id}`"
              :label="`${'　'.repeat(org.depth)}${org.name} (${org.code})`"
              :value="org.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="主组织" required>
          <el-select
            v-model="createForm.primaryOrgUnitId"
            clearable
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="org in createPrimaryOrgOptions"
              :key="`create-primary-org-${org.id}`"
              :label="`${'　'.repeat(org.depth)}${org.name} (${org.code})`"
              :value="org.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="createForm.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="createDialogVisible = false">取消</el-button>
        <el-button type="primary" v-perms="'btn:system-user:create'" @click="handleCreate">创建</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="editDialogVisible" title="编辑用户" width="660">
      <el-form label-width="110px">
        <el-form-item label="用户名">
          <el-input :model-value="editForm.username" disabled />
        </el-form-item>
        <el-form-item label="昵称" required>
          <el-input v-model="editForm.nickname" maxlength="100" />
        </el-form-item>
        <el-form-item label="头像">
          <el-input v-model="editForm.avatar" maxlength="500" />
        </el-form-item>
        <el-form-item label="角色" required>
          <el-select
            v-model="editForm.roles"
            multiple
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="role in activeRoleOptions"
              :key="`edit-role-${role.code}`"
              :label="`${role.name} (${role.code})`"
              :value="role.code"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关联组织" required>
          <el-select
            v-model="editForm.orgUnitIds"
            multiple
            filterable
            class="w-full"
            popper-class="config-select-popper"
            @change="handleEditOrgChange"
          >
            <el-option
              v-for="org in activeOrgUnitOptions"
              :key="`edit-org-${org.id}`"
              :label="`${'　'.repeat(org.depth)}${org.name} (${org.code})`"
              :value="org.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="主组织" required>
          <el-select
            v-model="editForm.primaryOrgUnitId"
            clearable
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="org in editPrimaryOrgOptions"
              :key="`edit-primary-org-${org.id}`"
              :label="`${'　'.repeat(org.depth)}${org.name} (${org.code})`"
              :value="org.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="editForm.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editDialogVisible = false">取消</el-button>
        <el-button type="primary" v-perms="'btn:system-user:update'" @click="handleUpdate">保存</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="resetPasswordDialogVisible" title="重置密码" width="500">
      <el-form label-width="100px">
        <el-form-item label="用户名">
          <el-input :model-value="resetPasswordForm.username" disabled />
        </el-form-item>
        <el-form-item label="新密码" required>
          <el-input
            v-model="resetPasswordForm.newPassword"
            type="password"
            show-password
            placeholder="至少8位"
          />
        </el-form-item>
        <el-form-item label="确认密码" required>
          <el-input
            v-model="resetPasswordForm.confirmPassword"
            type="password"
            show-password
            placeholder="再次输入新密码"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetPasswordDialogVisible = false">取消</el-button>
        <el-button type="primary" v-perms="'btn:system-user:reset-password'" @click="handleResetPassword">
          确认
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}
</style>
