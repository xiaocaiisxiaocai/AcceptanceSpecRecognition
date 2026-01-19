<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  batchAddKeywords,
  createKeyword,
  deleteKeyword,
  getKeywordList,
  updateKeyword,
  type Keyword
} from "@/api/keyword";

defineOptions({
  name: "Keywords"
});

const loading = ref(false);
const tableData = ref<Keyword[]>([]);
const total = ref(0);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: ""
});

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getKeywordList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载关键字失败");
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

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);

const formData = reactive({
  id: 0,
  word: ""
});

const handleAdd = () => {
  dialogTitle.value = "新增关键字";
  isEdit.value = false;
  formData.id = 0;
  formData.word = "";
  dialogVisible.value = true;
};

const handleEdit = (row: Keyword) => {
  dialogTitle.value = "编辑关键字";
  isEdit.value = true;
  formData.id = row.id;
  formData.word = row.word;
  dialogVisible.value = true;
};

const handleDelete = async (row: Keyword) => {
  try {
    await ElMessageBox.confirm(`确定删除关键字“${row.word}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteKeyword(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // cancelled
  }
};

const handleSubmit = async () => {
  if (!formData.word.trim()) {
    ElMessage.warning("请输入关键字");
    return;
  }
  try {
    const res = isEdit.value
      ? await updateKeyword(formData.id, { word: formData.word.trim() })
      : await createKeyword({ word: formData.word.trim() });
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

const batchDialogVisible = ref(false);
const batchText = ref("");

const normalizeWords = (text: string) => {
  return text
    .split(/[\n,，\t ]+/)
    .map(s => s.trim())
    .filter(Boolean);
};

const handleBatchAdd = () => {
  batchText.value = "";
  batchDialogVisible.value = true;
};

const handleBatchSubmit = async () => {
  const words = normalizeWords(batchText.value);
  if (words.length === 0) {
    ElMessage.warning("请输入关键字（可多行/逗号分隔）");
    return;
  }
  try {
    const res = await batchAddKeywords({ words });
    if (res.code === 0) {
      ElMessage.success(`新增 ${res.data} 个关键字`);
      batchDialogVisible.value = false;
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("批量新增失败");
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
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="按关键字搜索"
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
          <span>关键字管理</span>
          <div class="flex gap-2">
            <el-button @click="handleBatchAdd">批量新增</el-button>
            <el-button type="primary" @click="handleAdd">新增</el-button>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="word" label="关键字" min-width="260" />
        <el-table-column prop="createdAt" label="创建时间" width="180">
          <template #default="{ row }">
            {{ new Date(row.createdAt).toLocaleString() }}
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="handleEdit(row)">编辑</el-button>
            <el-button type="danger" link @click="handleDelete(row)">删除</el-button>
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

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="520">
      <el-form label-width="80px">
        <el-form-item label="关键字" required>
          <el-input v-model="formData.word" maxlength="100" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="batchDialogVisible" title="批量新增关键字" width="620">
      <el-form label-width="120px">
        <el-form-item label="关键字列表" required>
          <el-input
            v-model="batchText"
            type="textarea"
            :rows="10"
            placeholder="可多行/逗号分隔；自动去重"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="batchDialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleBatchSubmit">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.main {
  padding: 20px;
}
</style>

