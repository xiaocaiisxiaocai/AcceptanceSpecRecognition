<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  createSynonymGroup,
  deleteSynonymGroup,
  getSynonymGroupList,
  updateSynonymGroup,
  type SynonymGroup
} from "@/api/synonym";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "Synonyms"
});

const loading = ref(false);
const tableData = ref<SynonymGroup[]>([]);
const total = ref(0);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: ""
});

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getSynonymGroupList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载同义词失败");
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
  wordsText: ""
});

const canCreate = computed(() => hasPerms("btn:synonym-group:create"));
const canUpdate = computed(() => hasPerms("btn:synonym-group:update"));
const canDelete = computed(() => hasPerms("btn:synonym-group:delete"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

const normalizeWords = (text: string) => {
  return text
    .split(/[\n,，\t ]+/)
    .map(s => s.trim())
    .filter(Boolean);
};

const handleAdd = () => {
  if (!ensurePermission("btn:synonym-group:create", "权限不足，无法新增同义词组")) {
    return;
  }
  dialogTitle.value = "新增同义词组";
  isEdit.value = false;
  formData.id = 0;
  formData.wordsText = "";
  dialogVisible.value = true;
};

const handleEdit = (row: SynonymGroup) => {
  if (!ensurePermission("btn:synonym-group:update", "权限不足，无法编辑同义词组")) {
    return;
  }
  dialogTitle.value = "编辑同义词组";
  isEdit.value = true;
  formData.id = row.id;
  formData.wordsText = row.words.join("\n");
  dialogVisible.value = true;
};

const handleDelete = async (row: SynonymGroup) => {
  if (!ensurePermission("btn:synonym-group:delete", "权限不足，无法删除同义词组")) {
    return;
  }
  try {
    await ElMessageBox.confirm("确定删除该同义词组吗？", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteSynonymGroup(row.id);
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
  if (
    !ensurePermission(
      isEdit.value ? "btn:synonym-group:update" : "btn:synonym-group:create",
      isEdit.value ? "权限不足，无法保存同义词组" : "权限不足，无法新增同义词组"
    )
  ) {
    return;
  }
  const words = normalizeWords(formData.wordsText);
  if (words.length < 2) {
    ElMessage.warning("至少输入2个词（第一个为标准词）");
    return;
  }

  try {
    const res = isEdit.value
      ? await updateSynonymGroup(formData.id, { words })
      : await createSynonymGroup({ words });
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
  <div class="page">
    <div class="page-header">
      <div>
        <div class="page-title">同义词管理</div>
        <div class="page-subtitle">维护标准词与同义词关系</div>
      </div>
    </div>
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="按组内词搜索"
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
          <span>同义词管理</span>
          <el-button v-if="canCreate" type="primary" @click="handleAdd">
            新增
          </el-button>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column label="标准词" min-width="160">
          <template #default="{ row }">
            <el-tag type="success">{{ row.words[0] }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="同义词" min-width="360">
          <template #default="{ row }">
            <div class="flex flex-wrap gap-2">
              <el-tag v-for="w in row.words.slice(1)" :key="w" type="info">
                {{ w }}
              </el-tag>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="更新时间" width="180">
          <template #default="{ row }">
            {{ new Date((row.updatedAt ?? row.createdAt) as string).toLocaleString() }}
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasOperationActions"
          label="操作"
          width="150"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button v-if="canUpdate" type="primary" link @click="handleEdit(row)">
              编辑
            </el-button>
            <el-button v-if="canDelete" type="danger" link @click="handleDelete(row)">
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

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="620">
      <el-form label-width="120px">
        <el-form-item label="词列表" required>
          <el-input
            v-model="formData.wordsText"
            type="textarea"
            :rows="10"
            placeholder="每行一个词（或用逗号分隔）。第一行视为标准词，至少2个词。"
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

