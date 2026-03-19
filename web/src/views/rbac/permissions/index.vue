<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import { getAuthPermissionList, type AuthPermission } from "@/api/auth-permission";

defineOptions({
  name: "AuthPermissionsView"
});

type PermissionTypeFilter = "" | "0" | "1" | "2";

const loading = ref(false);
const permissions = ref<AuthPermission[]>([]);
const queryForm = reactive({
  keyword: "",
  permissionType: "" as PermissionTypeFilter
});

const permissionTypeOptions = [
  { label: "全部", value: "" },
  { label: "页面权限", value: "0" },
  { label: "按钮权限", value: "1" },
  { label: "接口权限", value: "2" }
];

const permissionTypeLabel = (permissionType: number) => {
  if (permissionType === 0) return "页面权限";
  if (permissionType === 1) return "按钮权限";
  if (permissionType === 2) return "接口权限";
  return "未知";
};

const loadData = async () => {
  loading.value = true;
  try {
    const permissionType =
      queryForm.permissionType === "" ? undefined : Number(queryForm.permissionType);
    const res = await getAuthPermissionList({
      keyword: queryForm.keyword.trim() || undefined,
      permissionType
    });
    if (res.code === 0) {
      permissions.value = (res.data ?? []).sort((a, b) => a.code.localeCompare(b.code));
    } else {
      ElMessage.error(res.message || "加载权限字典失败");
    }
  } catch {
    ElMessage.error("加载权限字典失败");
  } finally {
    loading.value = false;
  }
};

const handleSearch = () => {
  loadData();
};

const handleReset = () => {
  queryForm.keyword = "";
  queryForm.permissionType = "";
  loadData();
};

onMounted(loadData);
</script>

<template>
  <div class="page">
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="权限类型">
          <el-select
            v-model="queryForm.permissionType"
            class="w-[180px]"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="option in permissionTypeOptions"
              :key="`permission-type-${option.value}`"
              :label="option.label"
              :value="option.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关键词">
          <el-input
            v-model="queryForm.keyword"
            placeholder="权限编码/名称/资源/动作"
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
          <span>权限字典</span>
          <span class="text-[12px] text-[#909399]">共 {{ permissions.length }} 项</span>
        </div>
      </template>

      <el-table v-loading="loading" :data="permissions" stripe max-height="580">
        <el-table-column prop="code" label="权限编码" min-width="320" />
        <el-table-column prop="name" label="权限名称" min-width="220" />
        <el-table-column label="类型" width="120">
          <template #default="{ row }">
            <el-tag
              :type="
                row.permissionType === 0
                  ? 'success'
                  : row.permissionType === 1
                    ? 'warning'
                    : 'info'
              "
              size="small"
            >
              {{ permissionTypeLabel(row.permissionType) }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="resource" label="资源" min-width="140" />
        <el-table-column prop="action" label="动作" min-width="140" />
      </el-table>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
}
</style>
