<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessage } from "element-plus";
import {
  OperationType,
  getHistoryList,
  undoHistory,
  type OperationHistory
} from "@/api/history";

defineOptions({
  name: "OperationHistory"
});

const loading = ref(false);
const tableData = ref<OperationHistory[]>([]);
const total = ref(0);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  operationType: undefined as OperationType | undefined,
  canUndo: undefined as boolean | undefined,
  keyword: ""
});

const typeOptions = [
  { label: "导入", value: OperationType.Import },
  { label: "填充", value: OperationType.Fill },
  { label: "删除", value: OperationType.Delete }
];

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getHistoryList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载操作历史失败");
  } finally {
    loading.value = false;
  }
};

const handleSearch = () => {
  queryParams.page = 1;
  loadData();
};

const handleReset = () => {
  queryParams.page = 1;
  queryParams.keyword = "";
  queryParams.operationType = undefined;
  queryParams.canUndo = undefined;
  loadData();
};

const handleUndo = async (row: OperationHistory) => {
  try {
    const res = await undoHistory(row.id);
    if (res.code === 0) {
      ElMessage.success("撤销成功");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch (e: any) {
    // 目前后端返回 501
    ElMessage.error(e?.response?.data?.message ?? "撤销失败（功能尚未实现）");
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

onMounted(loadData);
</script>

<template>
  <div class="main">
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="类型">
          <el-select v-model="queryParams.operationType" clearable placeholder="全部">
            <el-option
              v-for="opt in typeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="可撤销">
          <el-select v-model="queryParams.canUndo" clearable placeholder="全部">
            <el-option :label="'是'" :value="true" />
            <el-option :label="'否'" :value="false" />
          </el-select>
        </el-form-item>
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="TargetFile/Details"
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
        <div class="flex justify-between items-center">
          <span>操作历史</span>
          <span class="text-sm text-gray-500">撤销接口后端目前为占位（501）</span>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="operationType" label="类型" width="100">
          <template #default="{ row }">
            {{ typeOptions.find(x => x.value === row.operationType)?.label }}
          </template>
        </el-table-column>
        <el-table-column prop="targetFile" label="目标文件" min-width="200" />
        <el-table-column prop="details" label="详情" min-width="320" />
        <el-table-column label="可撤销" width="90">
          <template #default="{ row }">
            <el-tag v-if="row.canUndo" type="warning">可撤销</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="createdAt" label="时间" width="180">
          <template #default="{ row }">
            {{ new Date(row.createdAt).toLocaleString() }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" fixed="right">
          <template #default="{ row }">
            <el-button
              type="warning"
              link
              :disabled="!row.canUndo"
              @click="handleUndo(row)"
            >
              撤销
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
  </div>
</template>

<style scoped>
.main {
  padding: 20px;
}
</style>

