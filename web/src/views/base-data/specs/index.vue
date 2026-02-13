<script setup lang="ts">
import { ref, onMounted, reactive, computed } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getSpecList,
  createSpec,
  updateSpec,
  deleteSpec,
  batchDeleteSpecs,
  type AcceptanceSpec,
  type SpecListRequest
} from "@/api/spec";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";

defineOptions({
  name: "AcceptanceSpecs"
});

// 数据列表
const tableData = ref<AcceptanceSpec[]>([]);
const loading = ref(false);
const total = ref(0);

// 选中的行
const selectedRows = ref<AcceptanceSpec[]>([]);

// 客户、制程、机型列表（用于下拉选择）
const customerList = ref<Customer[]>([]);
const processList = ref<Process[]>([]);
const machineModelList = ref<MachineModel[]>([]);

// 查询参数
const queryParams = reactive<SpecListRequest>({
  page: 1,
  pageSize: 20,
  keyword: "",
  customerId: undefined,
  processId: undefined,
  machineModelId: undefined
});

// 对话框
const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({
  id: 0,
  customerId: undefined as number | undefined,
  processId: undefined as number | undefined,
  machineModelId: undefined as number | undefined,
  project: "",
  specification: "",
  acceptance: "",
  remark: ""
});

// 详情对话框
const detailDialogVisible = ref(false);
const detailData = ref<AcceptanceSpec | null>(null);

// 加载客户列表
const loadCustomers = async () => {
  try {
    const res = await getCustomerList({ pageSize: 1000 });
    if (res.code === 0) {
      customerList.value = res.data.items;
    }
  } catch {
    // 忽略错误
  }
};

// 加载制程列表
const loadProcesses = async () => {
  try {
    const res = await getProcessList({ pageSize: 1000 });
    if (res.code === 0) {
      processList.value = res.data.items;
    }
  } catch {
    // 忽略错误
  }
};

// 加载机型列表
const loadMachineModels = async () => {
  try {
    const res = await getMachineModelList({ pageSize: 1000 });
    if (res.code === 0) {
      machineModelList.value = res.data.items;
    }
  } catch {
    // 忽略错误
  }
};

// 加载数据
const loadData = async () => {
  loading.value = true;
  try {
    const res = await getSpecList(queryParams);
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
  queryParams.customerId = undefined;
  queryParams.processId = undefined;
  queryParams.machineModelId = undefined;
  queryParams.page = 1;
  loadData();
};

// 客户筛选变化（客户与制程为独立基础数据，不做级联重置）
const handleCustomerChange = () => {};

// 表单客户变化（不做级联重置）
const handleFormCustomerChange = () => {};

// 新增
const handleAdd = () => {
  dialogTitle.value = "新增验收规格";
  isEdit.value = false;
  formData.id = 0;
  formData.customerId = undefined;
  formData.processId = undefined;
  formData.machineModelId = undefined;
  formData.project = "";
  formData.specification = "";
  formData.acceptance = "";
  formData.remark = "";
  dialogVisible.value = true;
};

// 编辑
const handleEdit = (row: AcceptanceSpec) => {
  dialogTitle.value = "编辑验收规格";
  isEdit.value = true;
  formData.id = row.id;
  formData.customerId = row.customerId;
  formData.processId = row.processId;
  formData.machineModelId = row.machineModelId;
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
  try {
    await ElMessageBox.confirm(
      `确定要删除项目"${row.project}"的验收规格吗？`,
      "提示",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
    const res = await deleteSpec(row.id);
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

// 批量删除
const handleBatchDelete = async () => {
  if (selectedRows.value.length === 0) {
    ElMessage.warning("请先选择要删除的规格");
    return;
  }
  try {
    await ElMessageBox.confirm(
      `确定要删除选中的 ${selectedRows.value.length} 条规格吗？`,
      "提示",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
    const ids = selectedRows.value.map(r => r.id);
    const res = await batchDeleteSpecs(ids);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      selectedRows.value = [];
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // 用户取消
  }
};

// 选择变化
const handleSelectionChange = (rows: AcceptanceSpec[]) => {
  selectedRows.value = rows;
};

// 提交表单
const handleSubmit = async () => {
  if (!formData.project.trim()) {
    ElMessage.warning("请输入项目名称");
    return;
  }
  if (!formData.specification.trim()) {
    ElMessage.warning("请输入规格内容");
    return;
  }
  if (!isEdit.value && !formData.customerId) {
    ElMessage.warning("请选择所属客户");
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
          customerId: formData.customerId!,
          processId: formData.processId || undefined,
          machineModelId: formData.machineModelId || undefined,
          project: formData.project,
          specification: formData.specification,
          acceptance: formData.acceptance || undefined,
          remark: formData.remark || undefined
        });
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
  loadCustomers();
  loadProcesses();
  loadMachineModels();
  loadData();
});
</script>

<template>
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">验收规格</div>
        <div class="page-subtitle">管理验收规格条目与详情</div>
      </div>
    </div>
    <!-- 搜索栏 -->
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="所属客户">
          <el-select
            v-model="queryParams.customerId"
            placeholder="全部客户"
            clearable
            class="search-select search-select--300"
            popper-class="app-select-popper"
            @change="handleCustomerChange"
          >
            <el-option
              v-for="item in customerList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="所属制程">
          <el-select
            v-model="queryParams.processId"
            placeholder="全部制程"
            clearable
            class="search-select search-select--300"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="item in processList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="所属机型">
          <el-select
            v-model="queryParams.machineModelId"
            placeholder="全部机型"
            clearable
            class="search-select search-select--300"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="item in machineModelList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="关键字">
          <el-input
            v-model="queryParams.keyword"
            placeholder="项目/规格/验收标准"
            clearable
            style="width: 180px"
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
          <span>验收规格列表</span>
          <div>
            <el-button
              type="danger"
              :disabled="selectedRows.length === 0"
              @click="handleBatchDelete"
              >批量删除</el-button
            >
            <el-button type="primary" @click="handleAdd">新增规格</el-button>
          </div>
        </div>
      </template>

      <el-table
        v-loading="loading"
        :data="tableData"
        stripe
        @selection-change="handleSelectionChange"
      >
        <el-table-column type="selection" width="50" />
        <el-table-column prop="id" label="ID" width="70" />
        <el-table-column prop="customerName" label="客户" width="120" />
        <el-table-column prop="processName" label="制程" width="120">
          <template #default="{ row }">
            <span v-if="row.processName">{{ row.processName }}</span>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="machineModelName" label="机型" width="120">
          <template #default="{ row }">
            <span v-if="row.machineModelName">{{ row.machineModelName }}</span>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="project" label="项目" min-width="150">
          <template #default="{ row }">
            <el-tooltip :content="row.project" placement="top">
              <span class="line-clamp-1">{{ row.project }}</span>
            </el-tooltip>
          </template>
        </el-table-column>
        <el-table-column
          prop="specification"
          label="规格内容"
          min-width="200"
        >
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
            <el-tooltip v-if="row.remark" :content="row.remark" placement="top">
              <span class="line-clamp-1">{{ row.remark }}</span>
            </el-tooltip>
            <span v-else class="text-gray-400">-</span>
          </template>
        </el-table-column>
        <el-table-column prop="importedAt" label="导入时间" width="160">
          <template #default="{ row }">
            {{ new Date(row.importedAt).toLocaleString() }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="handleView(row)"
              >查看</el-button
            >
            <el-button type="primary" link @click="handleEdit(row)"
              >编辑</el-button
            >
            <el-button type="danger" link @click="handleDelete(row)"
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
    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="600">
      <el-form label-width="100px">
        <el-form-item label="所属客户" required v-if="!isEdit">
          <el-select
            v-model="formData.customerId"
            placeholder="请选择客户"
            class="dialog-select dialog-select--320"
            popper-class="app-select-popper"
            @change="handleFormCustomerChange"
          >
            <el-option
              v-for="item in customerList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="所属制程" v-if="!isEdit">
          <el-select
            v-model="formData.processId"
            placeholder="请选择制程"
            class="dialog-select dialog-select--320"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="item in processList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="所属机型" v-if="!isEdit">
          <el-select
            v-model="formData.machineModelId"
            placeholder="请选择机型"
            class="dialog-select dialog-select--320"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="item in machineModelList"
              :key="item.id"
              :label="item.name"
              :value="item.id"
            />
          </el-select>
        </el-form-item>
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
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>

    <!-- 详情对话框 -->
    <el-dialog v-model="detailDialogVisible" title="规格详情" width="600">
      <el-descriptions v-if="detailData" :column="1" border>
        <el-descriptions-item label="ID">{{ detailData.id }}</el-descriptions-item>
        <el-descriptions-item label="客户">{{
          detailData.customerName
        }}</el-descriptions-item>
        <el-descriptions-item label="制程">{{
          detailData.processName || "-"
        }}</el-descriptions-item>
        <el-descriptions-item label="机型">{{
          detailData.machineModelName || "-"
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
          <div class="whitespace-pre-wrap">{{ detailData.remark || "-" }}</div>
        </el-descriptions-item>
        <el-descriptions-item label="导入时间">{{
          new Date(detailData.importedAt).toLocaleString()
        }}</el-descriptions-item>
      </el-descriptions>
      <template #footer>
        <el-button @click="detailDialogVisible = false">关闭</el-button>
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

.line-clamp-1 {
  display: -webkit-box;
  -webkit-line-clamp: 1;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
