<script setup lang="ts">
import { computed, ref, reactive, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getSpecList,
  detectSpecDuplicateGroups,
  createSpec,
  updateSpec,
  deleteSpec,
  batchDeleteSpecs,
  type AcceptanceSpec,
  type SpecListRequest,
  type SpecDuplicateDetectionResult
} from "@/api/spec";
import { hasPerms } from "@/utils/auth";
import SpecDuplicateDialog from "./SpecDuplicateDialog.vue";

const props = defineProps<{
  customerId: number;
  machineModelId?: number;
  processId?: number;
  customerName: string;
  machineModelName?: string;
  processName?: string;
}>();

const emit = defineEmits<{
  "data-change": [];
}>();

// 数据列表
const tableData = ref<AcceptanceSpec[]>([]);
const loading = ref(false);
const total = ref(0);
const selectedRows = ref<AcceptanceSpec[]>([]);

// 查询参数
const queryParams = reactive({
  page: 1,
  pageSize: 500,
  keyword: ""
});

// 对话框
const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({
  id: 0,
  project: "",
  specification: "",
  acceptance: "",
  remark: ""
});

// 详情对话框
const detailDialogVisible = ref(false);
const detailData = ref<AcceptanceSpec | null>(null);
const duplicateDialogVisible = ref(false);
const duplicateLoading = ref(false);
const duplicateResult = ref<SpecDuplicateDetectionResult | null>(null);

const canCreate = computed(() => hasPerms("btn:spec:create"));
const canUpdate = computed(() => hasPerms("btn:spec:update"));
const canDelete = computed(() => hasPerms("btn:spec:delete"));
const canBatchDelete = computed(() => hasPerms("btn:spec:delete-batch"));
const canInspectDuplicates = computed(() => hasPerms("api:spec:read"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const showToolbarRight = computed(
  () => canCreate.value || canBatchDelete.value || canInspectDuplicates.value
);
const actionColumnWidth = computed(() => {
  const visibleActionCount =
    1 + Number(canUpdate.value) + Number(canDelete.value);
  if (visibleActionCount <= 1) return 90;
  if (visibleActionCount === 2) return 130;
  return 170;
});

/** 构建请求参数，包含 IsNull 标志 */
const buildRequestParams = (): SpecListRequest => {
  const params: SpecListRequest = {
    page: queryParams.page,
    pageSize: queryParams.pageSize,
    customerId: props.customerId
  };
  if (queryParams.keyword) {
    params.keyword = queryParams.keyword;
  }
  // 区分"有值"和"值为 null"
  if (props.machineModelId != null) {
    params.machineModelId = props.machineModelId;
  } else {
    params.machineModelIdIsNull = true;
  }
  if (props.processId != null) {
    params.processId = props.processId;
  } else {
    params.processIdIsNull = true;
  }
  return params;
};

/** 加载数据 */
const loadData = async () => {
  loading.value = true;
  try {
    const res = await getSpecList(buildRequestParams());
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载数据失败");
  } finally {
    loading.value = false;
  }
};

/** 监听 props 变化时重新加载 */
watch(
  () => [props.customerId, props.machineModelId, props.processId],
  () => {
    queryParams.page = 1;
    queryParams.keyword = "";
    selectedRows.value = [];
    duplicateDialogVisible.value = false;
    duplicateResult.value = null;
    loadData();
  },
  { immediate: true }
);

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

// 新增（自动填充当前分组的客户/机型/制程）
const handleAdd = () => {
  if (!canCreate.value) {
    ElMessage.error("权限不足，无法新增规格");
    return;
  }
  dialogTitle.value = "新增验收规格";
  isEdit.value = false;
  formData.id = 0;
  formData.project = "";
  formData.specification = "";
  formData.acceptance = "";
  formData.remark = "";
  dialogVisible.value = true;
};

// 编辑
const handleEdit = (row: AcceptanceSpec) => {
  if (!canUpdate.value) {
    ElMessage.error("权限不足，无法编辑规格");
    return;
  }
  dialogTitle.value = "编辑验收规格";
  isEdit.value = true;
  formData.id = row.id;
  formData.project = row.project;
  formData.specification = row.specification;
  formData.acceptance = row.acceptance || "";
  formData.remark = row.remark || "";
  dialogVisible.value = true;
};

// 查看详情
const handleView = (row: AcceptanceSpec) => {
  detailData.value = row;
  detailDialogVisible.value = true;
};

// 删除
const handleDelete = async (row: AcceptanceSpec) => {
  if (!canDelete.value) {
    ElMessage.error("权限不足，无法删除规格");
    return;
  }
  try {
    await ElMessageBox.confirm(
      `确定要删除项目"${row.project}"的验收规格吗？`,
      "提示",
      { confirmButtonText: "确定", cancelButtonText: "取消", type: "warning" }
    );
    const res = await deleteSpec(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      loadData();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // 用户取消
  }
};

// 批量删除
const handleBatchDelete = async () => {
  if (!canBatchDelete.value) {
    ElMessage.error("权限不足，无法批量删除规格");
    return;
  }
  if (selectedRows.value.length === 0) {
    ElMessage.warning("请先选择要删除的规格");
    return;
  }
  try {
    await ElMessageBox.confirm(
      `确定要删除选中的 ${selectedRows.value.length} 条规格吗？`,
      "提示",
      { confirmButtonText: "确定", cancelButtonText: "取消", type: "warning" }
    );
    const ids = selectedRows.value.map(r => r.id);
    const res = await batchDeleteSpecs(ids);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      selectedRows.value = [];
      loadData();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // 用户取消
  }
};

const handleInspectDuplicates = async () => {
  if (!canInspectDuplicates.value) {
    ElMessage.error("权限不足，无法执行重复排查");
    return;
  }

  duplicateDialogVisible.value = true;
  duplicateLoading.value = true;
  duplicateResult.value = null;

  try {
    const res = await detectSpecDuplicateGroups({
      ...buildRequestParams(),
      maxGroups: 30
    });
    if (res.code === 0) {
      duplicateResult.value = res.data;
    } else {
      duplicateResult.value = null;
      ElMessage.error(res.message);
    }
  } catch {
    duplicateResult.value = null;
    ElMessage.error("重复排查失败");
  } finally {
    duplicateLoading.value = false;
  }
};

// 选择变化
const handleSelectionChange = (rows: AcceptanceSpec[]) => {
  selectedRows.value = rows;
};

// 提交表单
const handleSubmit = async () => {
  if (!canSubmit.value) {
    ElMessage.error("权限不足，无法提交当前操作");
    return;
  }
  if (!formData.project.trim()) {
    ElMessage.warning("请输入项目名称");
    return;
  }
  if (!formData.specification.trim()) {
    ElMessage.warning("请输入规格内容");
    return;
  }
  try {
    const res = isEdit.value
      ? await updateSpec(formData.id, {
          project: formData.project,
          specification: formData.specification,
          acceptance: formData.acceptance || undefined,
          remark: formData.remark || undefined
        })
      : await createSpec({
          customerId: props.customerId,
          processId: props.processId,
          machineModelId: props.machineModelId,
          project: formData.project,
          specification: formData.specification,
          acceptance: formData.acceptance || undefined,
          remark: formData.remark || undefined
        });
    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      loadData();
      emit("data-change");
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("操作失败");
  }
};

// 分页
const handlePageChange = (page: number) => {
  queryParams.page = page;
  loadData();
};

const handleSizeChange = (size: number) => {
  queryParams.pageSize = size;
  queryParams.page = 1;
  loadData();
};

/** 当前分组描述文字 */
const groupLabel = () => {
  const parts = [props.customerName];
  parts.push(props.machineModelName || "未指定机型");
  parts.push(props.processName || "未指定制程");
  return parts.join(" / ");
};
</script>

<template>
  <div class="spec-table">
    <!-- 当前分组标签 -->
    <div class="group-label">
      <el-tag type="info" size="large" effect="plain">
        {{ groupLabel() }}
      </el-tag>
    </div>

    <!-- 操作栏 -->
    <div class="toolbar">
      <div class="toolbar-left">
        <el-input
          v-model="queryParams.keyword"
          placeholder="项目/规格/验收标准"
          clearable
          style="width: 220px"
          @keyup.enter="handleSearch"
        />
        <el-button type="primary" @click="handleSearch">搜索</el-button>
        <el-button @click="handleReset">重置</el-button>
      </div>
      <div v-if="showToolbarRight" class="toolbar-right">
        <el-button v-if="canInspectDuplicates" @click="handleInspectDuplicates">
          重复排查
        </el-button>
        <el-button v-if="canCreate" type="primary" @click="handleAdd">
          新增规格
        </el-button>
        <el-button
          v-if="canBatchDelete"
          type="danger"
          :disabled="selectedRows.length === 0"
          @click="handleBatchDelete"
        >
          批量删除
        </el-button>
      </div>
    </div>

    <!-- 数据表格（去掉客户/制程/机型列） -->
    <div class="table-main">
      <el-table
        v-loading="loading"
        :data="tableData"
        stripe
        height="100%"
        @selection-change="handleSelectionChange"
      >
        <el-table-column v-if="canBatchDelete" type="selection" width="50" />
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column prop="project" label="项目" min-width="150">
          <template #default="{ row }">
            <el-tooltip :content="row.project" placement="top">
              <span class="line-clamp-1">{{ row.project }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column prop="specification" label="规格内容" min-width="200">
          <template #default="{ row }">
            <el-tooltip :content="row.specification" placement="top">
              <span class="line-clamp-1">{{ row.specification }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column prop="acceptance" label="验收标准" min-width="150">
          <template #default="{ row }">
            <el-tooltip
              v-if="row.acceptance"
              :content="row.acceptance"
              placement="top"
            >
              <span class="line-clamp-1">{{ row.acceptance }}</span>
            </el-tooltip>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="remark" label="备注" min-width="150">
          <template #default="{ row }">
            <el-tooltip
              v-if="row.remark"
              :content="row.remark"
              placement="top"
            >
              <span class="line-clamp-1">{{ row.remark }}</span>
            </el-tooltip>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column
          label="操作"
          :width="actionColumnWidth"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button type="primary" link @click="handleView(row)">
              查看
            </el-button>
            <el-button v-if="canUpdate" type="primary" link @click="handleEdit(row)">
              编辑
            </el-button>
            <el-button v-if="canDelete" type="danger" link @click="handleDelete(row)">
              删除
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <!-- 分页 -->
    <div class="pagination">
      <el-pagination
        v-model:current-page="queryParams.page"
        v-model:page-size="queryParams.pageSize"
        :page-sizes="[100, 200, 500, 1000]"
        :total="total"
        layout="total, sizes, prev, pager, next, jumper"
        @size-change="handleSizeChange"
        @current-change="handlePageChange"
      />
    </div>

    <!-- 新增/编辑对话框 -->
    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="600">
      <el-form label-width="100px">
        <el-form-item label="项目名称" required>
          <el-input
            v-model="formData.project"
            placeholder="请输入项目名称"
            maxlength="500"
          />
        </el-form-item>
        <el-form-item label="规格内容" required>
          <el-input
            v-model="formData.specification"
            type="textarea"
            :rows="3"
            placeholder="请输入规格内容"
          />
        </el-form-item>
        <el-form-item label="验收标准">
          <el-input
            v-model="formData.acceptance"
            type="textarea"
            :rows="2"
            placeholder="请输入验收标准（可选）"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input
            v-model="formData.remark"
            type="textarea"
            :rows="2"
            placeholder="请输入备注（可选）"
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

    <!-- 详情对话框 -->
    <el-dialog v-model="detailDialogVisible" title="规格详情" width="600">
      <el-descriptions v-if="detailData" :column="1" border>
        <el-descriptions-item label="ID">{{
          detailData.id
        }}</el-descriptions-item>
        <el-descriptions-item label="客户">{{
          customerName
        }}</el-descriptions-item>
        <el-descriptions-item label="机型">{{
          machineModelName || "-"
        }}</el-descriptions-item>
        <el-descriptions-item label="制程">{{
          processName || "-"
        }}</el-descriptions-item>
        <el-descriptions-item label="项目">{{
          detailData.project
        }}</el-descriptions-item>
        <el-descriptions-item label="规格内容">
          <div class="whitespace-pre-wrap">{{ detailData.specification }}</div>
        </el-descriptions-item>
        <el-descriptions-item label="验收标准">
          <div class="whitespace-pre-wrap">
            {{ detailData.acceptance || "-" }}
          </div>
        </el-descriptions-item>
        <el-descriptions-item label="备注">
          <div class="whitespace-pre-wrap">
            {{ detailData.remark || "-" }}
          </div>
        </el-descriptions-item>
        <el-descriptions-item label="导入时间">{{
          new Date(detailData.importedAt).toLocaleString()
        }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button @click="detailDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <SpecDuplicateDialog
      v-model="duplicateDialogVisible"
      :loading="duplicateLoading"
      :result="duplicateResult"
      :group-label="groupLabel()"
    />
  </div>
</template>

<style scoped>
.spec-table {
  display: flex;
  flex-direction: column;
  gap: 12px;
  height: 100%;
  min-height: 0;
}

.group-label {
  margin-bottom: 4px;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.toolbar-left,
.toolbar-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.table-main {
  flex: 1;
  min-height: 0;
}

.pagination {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
  flex-shrink: 0;
}

.line-clamp-1 {
  display: -webkit-box;
  -webkit-line-clamp: 1;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
