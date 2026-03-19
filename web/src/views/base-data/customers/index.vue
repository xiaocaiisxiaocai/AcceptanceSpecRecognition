<script setup lang="ts">
import { computed, ref, onMounted, reactive } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getCustomerList,
  createCustomer,
  updateCustomer,
  deleteCustomer,
  type Customer,
  type PagedData
} from "@/api/customer";
import { hasPerms } from "@/utils/auth";

defineOptions({
  name: "Customers"
});

// 数据列表
const tableData = ref<Customer[]>([]);
const loading = ref(false);
const total = ref(0);

// 查询参数
const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: ""
});

// 对话框
const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({
  id: 0,
  name: ""
});

const canCreate = computed(() => hasPerms("btn:customer:create"));
const canUpdate = computed(() => hasPerms("btn:customer:update"));
const canDelete = computed(() => hasPerms("btn:customer:delete"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

// 加载数据
const loadData = async () => {
  loading.value = true;
  try {
    const res = await getCustomerList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    ElMessage.error("加载数据失败");
  } finally {
    loading.value = false;
  }
};

// 搜索
const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};

// 重置
const handleReset = () => {
  queryParams.keyword = "";
  queryParams.page = 1;
  loadData();
};

// 新增
const handleAdd = () => {
  if (!canCreate.value) {
    ElMessage.error("权限不足，无法新增客户");
    return;
  }
  dialogTitle.value = "新增客户";
  isEdit.value = false;
  formData.id = 0;
  formData.name = "";
  dialogVisible.value = true;
};

// 编辑
const handleEdit = (row: Customer) => {
  if (!canUpdate.value) {
    ElMessage.error("权限不足，无法编辑客户");
    return;
  }
  dialogTitle.value = "编辑客户";
  isEdit.value = true;
  formData.id = row.id;
  formData.name = row.name;
  dialogVisible.value = true;
};

// 删除
const handleDelete = async (row: Customer) => {
  if (!canDelete.value) {
    ElMessage.error("权限不足，无法删除客户");
    return;
  }
  try {
    await ElMessageBox.confirm(`确定要删除客户"${row.name}"吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteCustomer(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // 用户取消
  }
};

// 提交表单
const handleSubmit = async () => {
  if (!canSubmit.value) {
    ElMessage.error("权限不足，无法提交当前操作");
    return;
  }
  if (!formData.name.trim()) {
    ElMessage.warning("请输入客户名称");
    return;
  }
  try {
    const res = isEdit.value
      ? await updateCustomer(formData.id, { name: formData.name })
      : await createCustomer({ name: formData.name });
    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("操作失败");
  }
};

// 分页变化
const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadData();
};

// 初始化
onMounted(() => {
  loadData();
});
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">客户管理</div>
        <div class="page-subtitle">维护客户信息，支持搜索与编辑</div>
      </div>
    </div>
    <!-- 搜索栏 -->
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="客户名称">
          <el-input
            v-model="queryParams.keyword"
            placeholder="请输入客户名称"
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

    <!-- 数据表格 -->
    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>客户列表</span>
          <el-button v-if="canCreate" type="primary" @click="handleAdd">
            新增客户
          </el-button>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="name" label="客户名称" min-width="200" />
        <el-table-column prop="createdAt" label="创建时间" width="180">
          <template #default="{ row }">
            {{ new Date(row.createdAt).toLocaleString() }}
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasOperationActions"
          label="操作"
          width="150"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button v-if="canUpdate" type="primary" link @click="handleEdit(row)"
              >编辑</el-button
            >
            <el-button v-if="canDelete" type="danger" link @click="handleDelete(row)"
              >删除</el-button
            >
          </template>
        </el-table-column>
      </el-table>

      <!-- 分页 -->
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

    <!-- 新增/编辑对话框 -->
    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="500">
      <el-form label-width="80px">
        <el-form-item label="客户名称" required>
          <el-input
            v-model="formData.name"
            placeholder="请输入客户名称"
            maxlength="100"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="handleSubmit">
          确定
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
