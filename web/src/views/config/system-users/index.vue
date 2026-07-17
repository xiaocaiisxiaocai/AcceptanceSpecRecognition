<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
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
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import {
  requiredSelectionRule,
  requiredTrimmedRule,
  validateForm
} from "@/utils/form-rules";

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
  pageSize: 50,
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
  roleCode: "",
  orgUnitId: null as number | null,
  isActive: true
});

const editForm = reactive({
  id: 0,
  username: "",
  nickname: "",
  avatar: "",
  roleCode: "",
  orgUnitId: null as number | null,
  isActive: true
});

const resetPasswordForm = reactive({
  userId: 0,
  username: "",
  newPassword: "",
  confirmPassword: ""
});

const createFormRef = ref<FormInstance>();
const editFormRef = ref<FormInstance>();
const resetPasswordFormRef = ref<FormInstance>();

const createFormRules: FormRules<typeof createForm> = {
  username: [
    requiredTrimmedRule("请输入用户名"),
    {
      pattern: /^[A-Za-z0-9._-]{3,64}$/,
      message: "用户名仅支持字母、数字、点、下划线、中划线，长度3-64",
      trigger: ["blur", "change"]
    }
  ],
  password: [
    { required: true, message: "请输入密码", trigger: ["blur", "change"] },
    { min: 4, message: "密码长度至少4位", trigger: ["blur", "change"] }
  ],
  nickname: [requiredTrimmedRule("请输入昵称")],
  roleCode: [requiredSelectionRule("请选择一个角色")],
  orgUnitId: [requiredSelectionRule("请选择一个组织")]
};

const editFormRules: FormRules<typeof editForm> = {
  nickname: [requiredTrimmedRule("请输入昵称")],
  roleCode: [requiredSelectionRule("请选择一个角色")],
  orgUnitId: [requiredSelectionRule("请选择一个组织")]
};

const resetPasswordFormRules: FormRules<typeof resetPasswordForm> = {
  newPassword: [
    { required: true, message: "请输入新密码", trigger: ["blur", "change"] },
    { min: 4, message: "新密码长度至少4位", trigger: ["blur", "change"] }
  ],
  confirmPassword: [
    {
      validator: (_rule, value, callback) => {
        if (!value) callback(new Error("请再次输入新密码"));
        else if (value !== resetPasswordForm.newPassword) {
          callback(new Error("两次输入的密码不一致"));
        } else callback();
      },
      trigger: ["blur", "change"]
    }
  ]
};

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

const getDefaultRoleCode = () => {
  const preferred = activeRoleOptions.value.find(
    item => item.code === "common"
  );
  if (preferred) return preferred.code;
  const first = activeRoleOptions.value[0];
  return first?.code ?? "";
};

const getDefaultOrgId = () => {
  const root = activeOrgUnitOptions.value.find(
    item => item.unitType === 0 && item.parentId == null
  );
  if (root) return root.id;

  const first = activeOrgUnitOptions.value[0];
  return first?.id;
};

const loadRoleOptions = async () => {
  const res = await getAuthRoleList();
  if (res.code === 0) {
    roleOptions.value = (res.data ?? []).sort((a, b) =>
      a.code.localeCompare(b.code)
    );
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
  queryParams.pageSize = 50;
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
  const defaultOrgId = getDefaultOrgId();
  createForm.username = "";
  createForm.password = "";
  createForm.nickname = "";
  createForm.avatar = "";
  createForm.roleCode = getDefaultRoleCode();
  createForm.orgUnitId = defaultOrgId ?? null;
  createForm.isActive = true;
  createDialogVisible.value = true;
};

const openEditDialog = (row: SystemUser) => {
  editForm.id = row.id;
  editForm.username = row.username;
  editForm.nickname = row.nickname;
  editForm.avatar = row.avatar ?? "";
  editForm.roleCode = row.roleCode ?? "";
  editForm.orgUnitId = row.orgUnitId ?? null;
  editForm.isActive = row.isActive;
  editDialogVisible.value = true;
};

const handleCreate = async () => {
  if (!(await validateForm(createFormRef.value))) return;

  const username = createForm.username.trim();
  const password = createForm.password;
  const nickname = createForm.nickname.trim();
  const roleCode = createForm.roleCode.trim();

  const payload: CreateSystemUserRequest = {
    username,
    password,
    nickname,
    avatar: createForm.avatar.trim() || "",
    roleCode,
    orgUnitId: createForm.orgUnitId,
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
  if (!(await validateForm(editFormRef.value))) return;

  const nickname = editForm.nickname.trim();
  const roleCode = editForm.roleCode.trim();
  const payload: UpdateSystemUserRequest = {
    nickname,
    avatar: editForm.avatar.trim() || "",
    roleCode,
    orgUnitId: editForm.orgUnitId,
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
  if (!(await validateForm(resetPasswordFormRef.value))) return;

  const newPassword = resetPasswordForm.newPassword;

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
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除用户失败"));
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
    updatingStatusIds.value = updatingStatusIds.value.filter(
      id => id !== row.id
    );
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

const formatRoleLabel = (roleCode?: string) => {
  if (!roleCode) return "-";
  const roleName = roleNameMap.value.get(roleCode);
  return roleName ? `${roleName} (${roleCode})` : roleCode;
};

const formatOrgLabel = (orgUnitId: number) => {
  const org = orgUnitMap.value.get(orgUnitId);
  if (!org) return `组织#${orgUnitId}`;
  return `${org.name} (${org.code})`;
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
                  v-model="queryParams.keyword"
                  placeholder="用户名/昵称"
                  clearable
                  @keyup.enter="handleSearch"
                />
              </el-form-item>
              <el-form-item label="状态">
                <el-select
                  v-model="queryParams.status"
                  class="search-select search-select--200"
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
            <el-button
              v-perms="'btn:system-user:create'"
              type="primary"
              @click="openCreateDialog"
            >
              新增用户
            </el-button>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column
          prop="username"
          label="用户名"
          min-width="min(140px, calc(100vw - 32px))"
        />
        <el-table-column
          prop="nickname"
          label="昵称"
          min-width="min(120px, calc(100vw - 32px))"
        />
        <el-table-column
          label="角色"
          min-width="min(220px, calc(100vw - 32px))"
        >
          <template #default="{ row }">
            <el-tag
              v-if="row.roleCode"
              type="success"
              class="mr-1 mb-1"
              size="small"
            >
              {{ formatRoleLabel(row.roleCode) }}
            </el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column
          label="组织"
          min-width="min(240px, calc(100vw - 32px))"
        >
          <template #default="{ row }">
            <el-tag
              v-if="row.orgUnitId"
              type="warning"
              class="mr-1 mb-1"
              size="small"
            >
              {{ formatOrgLabel(row.orgUnitId) }}
            </el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column label="状态" width="min(120px, calc(100vw - 32px))">
          <template #default="{ row }">
            <el-switch
              :model-value="row.isActive"
              :loading="updatingStatusIds.includes(row.id)"
              :disabled="
                !canDisable(row) || !hasPerms('btn:system-user:update-status')
              "
              @update:model-value="
                value => handleToggleStatus(row, value === true)
              "
            />
          </template>
        </el-table-column>
        <el-table-column
          label="更新时间"
          width="min(180px, calc(100vw - 32px))"
        >
          <template #default="{ row }">
            {{ formatDateTime(row.updatedAt ?? row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column
          label="操作"
          width="min(300px, calc(100vw - 32px))"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button
              v-perms="'btn:system-user:update'"
              type="primary"
              link
              @click="openEditDialog(row)"
            >
              编辑
            </el-button>
            <el-button
              v-perms="'btn:system-user:reset-password'"
              type="warning"
              link
              @click="openResetPasswordDialog(row)"
            >
              重置密码
            </el-button>
            <el-button
              v-perms="'btn:system-user:delete'"
              type="danger"
              link
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

    <el-dialog
      v-model="createDialogVisible"
      title="新增用户"
      width="min(640px, calc(100vw - 32px))"
    >
      <el-form
        ref="createFormRef"
        :model="createForm"
        :rules="createFormRules"
        label-width="110px"
        status-icon
      >
        <el-form-item label="用户名" prop="username">
          <el-input
            v-model="createForm.username"
            maxlength="64"
            placeholder="3-64位，支持字母/数字/._-"
          />
        </el-form-item>
        <el-form-item label="密码" prop="password">
          <el-input
            v-model="createForm.password"
            type="password"
            show-password
            placeholder="至少4位"
          />
        </el-form-item>
        <el-form-item label="昵称" prop="nickname">
          <el-input v-model="createForm.nickname" maxlength="100" />
        </el-form-item>
        <el-form-item label="头像">
          <el-input v-model="createForm.avatar" maxlength="500" />
        </el-form-item>
        <el-form-item label="角色" prop="roleCode">
          <el-select
            v-model="createForm.roleCode"
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
        <el-form-item label="所属组织" prop="orgUnitId">
          <el-select
            v-model="createForm.orgUnitId"
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="org in activeOrgUnitOptions"
              :key="`create-org-${org.id}`"
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
        <el-button
          v-perms="'btn:system-user:create'"
          type="primary"
          @click="handleCreate"
          >创建</el-button
        >
      </template>
    </el-dialog>

    <el-dialog
      v-model="editDialogVisible"
      title="编辑用户"
      width="min(640px, calc(100vw - 32px))"
    >
      <el-form
        ref="editFormRef"
        :model="editForm"
        :rules="editFormRules"
        label-width="110px"
        status-icon
      >
        <el-form-item label="用户名">
          <el-input :model-value="editForm.username" disabled />
        </el-form-item>
        <el-form-item label="昵称" prop="nickname">
          <el-input v-model="editForm.nickname" maxlength="100" />
        </el-form-item>
        <el-form-item label="头像">
          <el-input v-model="editForm.avatar" maxlength="500" />
        </el-form-item>
        <el-form-item label="角色" prop="roleCode">
          <el-select
            v-model="editForm.roleCode"
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
        <el-form-item label="所属组织" prop="orgUnitId">
          <el-select
            v-model="editForm.orgUnitId"
            filterable
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="org in activeOrgUnitOptions"
              :key="`edit-org-${org.id}`"
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
        <el-button
          v-perms="'btn:system-user:update'"
          type="primary"
          @click="handleUpdate"
          >保存</el-button
        >
      </template>
    </el-dialog>

    <el-dialog
      v-model="resetPasswordDialogVisible"
      title="重置密码"
      width="min(480px, calc(100vw - 32px))"
    >
      <el-form
        ref="resetPasswordFormRef"
        :model="resetPasswordForm"
        :rules="resetPasswordFormRules"
        label-width="100px"
        status-icon
      >
        <el-form-item label="用户名">
          <el-input :model-value="resetPasswordForm.username" disabled />
        </el-form-item>
        <el-form-item label="新密码" prop="newPassword">
          <el-input
            v-model="resetPasswordForm.newPassword"
            type="password"
            show-password
            placeholder="至少4位"
          />
        </el-form-item>
        <el-form-item label="确认密码" prop="confirmPassword">
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
        <el-button
          v-perms="'btn:system-user:reset-password'"
          type="primary"
          @click="handleResetPassword"
        >
          确认
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 0;
}
</style>
