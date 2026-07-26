<script setup lang="ts">
import { computed, ref, onMounted, reactive } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  getProcessList,
  createProcess,
  updateProcess,
  deleteProcess,
  batchDeleteProcesses,
  type Process,
  type ProcessListRequest
} from "@/api/process";
import { hasPerms } from "@/utils/auth";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import { requiredTrimmedRule, validateForm } from "@/utils/form-rules";

defineOptions({
  name: "Processes"
});

const tableData = ref<Process[]>([]);
const loading = ref(false);
const total = ref(0);
const tableRef = ref();

const queryParams = reactive<ProcessListRequest>({
  page: 1,
  pageSize: 50,
  keyword: ""
});

const selectedIds = ref<number[]>([]);
const hasSelected = computed(() => selectedIds.value.length > 0);

const handleSelectionChange = (rows: Process[]) => {
  selectedIds.value = rows.map(r => r.id);
};

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({ id: 0, name: "" });
const formRef = ref<FormInstance>();
const formRules: FormRules<typeof formData> = {
  name: [requiredTrimmedRule("请输入制程名称")]
};

const canCreate = computed(() => hasPerms("btn:process:create"));
const canUpdate = computed(() => hasPerms("btn:process:update"));
const canDelete = computed(() => hasPerms("btn:process:delete"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getProcessList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "加载数据失败"));
  } finally {
    loading.value = false;
  }
};

const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};
const handleReset = () => {
  queryParams.keyword = "";
  queryParams.page = 1;
  loadData();
};

const handleAdd = () => {
  if (!canCreate.value) {
    ElMessage.error("权限不足，无法新增制程");
    return;
  }
  dialogTitle.value = "新增制程";
  isEdit.value = false;
  formData.id = 0;
  formData.name = "";
  dialogVisible.value = true;
};

const handleEdit = (row: Process) => {
  if (!canUpdate.value) {
    ElMessage.error("权限不足，无法编辑制程");
    return;
  }
  dialogTitle.value = "编辑制程";
  isEdit.value = true;
  formData.id = row.id;
  formData.name = row.name;
  dialogVisible.value = true;
};

const handleDelete = async (row: Process) => {
  if (!canDelete.value) {
    ElMessage.error("权限不足，无法删除制程");
    return;
  }
  if (row.specCount > 0) {
    ElMessage.warning(
      `该制程下还有 ${row.specCount} 条关联验收规格，无法删除，请先清理关联数据`
    );
    return;
  }
  try {
    await ElMessageBox.confirm(`确定要删除制程"${row.name}"吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteProcess(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "删除失败"));
  }
};

const handleBatchDelete = async () => {
  if (!canDelete.value) {
    ElMessage.error("权限不足，无法删除制程");
    return;
  }
  if (!hasSelected.value) {
    ElMessage.warning("请先选择要删除的制程");
    return;
  }
  try {
    await ElMessageBox.confirm(
      `确定要删除选中的 ${selectedIds.value.length} 个制程吗？`,
      "批量删除",
      { confirmButtonText: "确定", cancelButtonText: "取消", type: "warning" }
    );
    const res = await batchDeleteProcesses(selectedIds.value);
    if (res.code === 0) {
      const failures = res.data?.failures ?? [];
      if (failures.length > 0) {
        ElMessage.warning(res.message || "部分制程删除失败");
      } else {
        ElMessage.success(res.message || "批量删除成功");
      }
      selectedIds.value = [];
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "批量删除失败"));
  }
};

const handleSubmit = async () => {
  if (!canSubmit.value) {
    ElMessage.error("权限不足，无法提交当前操作");
    return;
  }
  if (!(await validateForm(formRef.value))) return;
  try {
    const res = isEdit.value
      ? await updateProcess(formData.id, { name: formData.name })
      : await createProcess({ name: formData.name });
    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    ElMessage.error(getRequestErrorMessage(error, "操作失败"));
  }
};

const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};
const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadData();
};

onMounted(() => {
  loadData();
});
</script>

<template>
  <div class="page page--fill simple-crud-page">
    <el-card class="table-card" shadow="never">
      <template #header>
        <div class="simple-crud-toolbar">
          <div class="simple-crud-toolbar__right">
            <el-form class="simple-crud-search" :inline="true">
              <el-form-item label="制程名称">
                <el-input
                  v-model="queryParams.keyword"
                  placeholder="请输入制程名称"
                  clearable
                  @keyup.enter="handleSearch"
                />
              </el-form-item>
              <el-form-item>
                <el-button type="primary" @click="handleSearch">搜索</el-button>
                <el-button @click="handleReset">重置</el-button>
              </el-form-item>
            </el-form>
            <div class="simple-crud-actions">
              <el-button
                v-if="canDelete && hasSelected"
                type="danger"
                @click="handleBatchDelete"
              >
                批量删除 ({{ selectedIds.length }})
              </el-button>
              <el-button v-if="canCreate" type="primary" @click="handleAdd"
                >新增制程</el-button
              >
            </div>
          </div>
        </div>
      </template>

      <div class="table-region">
        <el-table
          ref="tableRef"
          v-loading="loading"
          :data="tableData"
          height="100%"
          stripe
          @selection-change="handleSelectionChange"
        >
          <el-table-column v-if="canDelete" type="selection" width="50" />
          <el-table-column prop="id" label="ID" width="80" />
          <el-table-column prop="name" label="制程名称" min-width="200" />
          <el-table-column prop="createdAt" label="创建时间" width="180">
            <template #default="{ row }">{{
              new Date(row.createdAt).toLocaleString()
            }}</template>
          </el-table-column>
          <el-table-column
            v-if="hasOperationActions"
            label="操作"
            width="150"
            fixed="right"
          >
            <template #default="{ row }">
              <el-button
                v-if="canUpdate"
                type="primary"
                link
                @click="handleEdit(row)"
                >编辑</el-button
              >
              <el-button
                v-if="canDelete"
                type="danger"
                link
                @click="handleDelete(row)"
                >删除</el-button
              >
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="queryParams.page"
          v-model:page-size="queryParams.pageSize"
          :page-sizes="[20, 50, 100, 200]"
          :total="total"
          layout="total, sizes, prev, pager, next, jumper"
          @size-change="handleSizeChange"
          @current-change="handlePageChange"
        />
      </div>
    </el-card>

    <el-dialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="min(480px, calc(100vw - 32px))"
    >
      <el-form
        ref="formRef"
        :model="formData"
        :rules="formRules"
        label-width="80px"
        status-icon
      >
        <el-form-item label="制程名称" prop="name">
          <el-input
            v-model="formData.name"
            placeholder="请输入制程名称"
            maxlength="100"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="handleSubmit"
          >确定</el-button
        >
      </template>
    </el-dialog>
  </div>
</template>
