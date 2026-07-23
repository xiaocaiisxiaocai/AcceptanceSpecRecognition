<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  getAuthPermissionList,
  type AuthPermission
} from "@/api/auth-permission";

defineOptions({
  name: "AuthPermissionsView"
});

type PermissionTypeFilter = "" | "0" | "1" | "2" | "3";

const loading = ref(false);
const permissions = ref<AuthPermission[]>([]);
const currentPage = ref(1);
const pageSize = ref(50);
const queryForm = reactive({
  keyword: "",
  permissionType: "" as PermissionTypeFilter
});

const permissionTypeOptions = [
  { label: "全部", value: "" },
  { label: "页面权限", value: "0" },
  { label: "按钮权限", value: "1" },
  { label: "接口权限", value: "2" },
  { label: "菜单权限", value: "3" }
];

const pagedPermissions = computed(() => {
  const start = (currentPage.value - 1) * pageSize.value;
  return permissions.value.slice(start, start + pageSize.value);
});

const permissionTypeLabel = (permissionType: number) => {
  if (permissionType === 0) return "页面权限";
  if (permissionType === 1) return "按钮权限";
  if (permissionType === 2) return "接口权限";
  if (permissionType === 3) return "菜单权限";
  return "未知";
};

const loadData = async () => {
  loading.value = true;
  try {
    const permissionType =
      queryForm.permissionType === ""
        ? undefined
        : Number(queryForm.permissionType);
    const res = await getAuthPermissionList({
      keyword: queryForm.keyword.trim() || undefined,
      permissionType
    });
    if (res.code === 0) {
      permissions.value = (res.data ?? []).sort((a, b) =>
        a.code.localeCompare(b.code)
      );
      currentPage.value = 1;
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

const handlePageSizeChange = () => {
  currentPage.value = 1;
};

onMounted(loadData);
</script>

<template>
  <div class="page page--fill config-page permissions-page">
    <el-card class="table-card" shadow="never">
      <template #header>
        <div class="list-card-toolbar">
          <div class="list-card-toolbar__right">
            <span class="text-[12px] text-[var(--app-text-disabled)]"
              >共 {{ permissions.length }} 项</span
            >
            <el-form :inline="true" class="filter-form">
              <el-form-item label="权限类型">
                <el-select
                  v-model="queryForm.permissionType"
                  class="search-select search-select--200"
                  popper-class="app-select-popper"
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
          </div>
        </div>
      </template>

      <div class="table-region">
        <el-table
          v-loading="loading"
          :data="pagedPermissions"
          stripe
          height="100%"
        >
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
                      : row.permissionType === 2
                        ? 'info'
                        : 'danger'
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
      </div>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="currentPage"
          v-model:page-size="pageSize"
          :page-sizes="[20, 50, 100]"
          :total="permissions.length"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="handlePageSizeChange"
        />
      </div>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 0;
}
</style>
