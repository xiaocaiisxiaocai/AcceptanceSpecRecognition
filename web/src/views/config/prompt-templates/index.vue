<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  createPromptTemplate,
  deletePromptTemplate,
  getDefaultPromptTemplate,
  getPromptTemplateList,
  setDefaultPromptTemplate,
  updatePromptTemplate,
  type PromptTemplate
} from "@/api/prompt-template";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "PromptTemplates"
});

const loading = ref(false);
const tableData = ref<PromptTemplate[]>([]);
const total = ref(0);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: ""
});

const canLoadDefault = computed(() => hasPerms("btn:prompt-template:default"));
const canCreate = computed(() => hasPerms("btn:prompt-template:create"));
const canUpdate = computed(() => hasPerms("btn:prompt-template:update"));
const canDelete = computed(() => hasPerms("btn:prompt-template:delete"));
const canSetDefault = computed(() => hasPerms("btn:prompt-template:set-default"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasOperationActions = computed(
  () => canUpdate.value || canDelete.value || canSetDefault.value
);

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getPromptTemplateList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载Prompt模板失败");
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

const handleLoadDefault = async () => {
  if (
    !ensurePermission("btn:prompt-template:default", "权限不足，无法加载默认模板")
  ) {
    return;
  }
  try {
    const res = await getDefaultPromptTemplate();
    if (res.code === 0) {
      ElMessage.success("默认模板已就绪");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载默认模板失败");
  }
};

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const formData = reactive({
  id: 0,
  name: "",
  content: "",
  isDefault: false
});

const handleAdd = () => {
  if (!ensurePermission("btn:prompt-template:create", "权限不足，无法新增Prompt模板")) {
    return;
  }
  dialogTitle.value = "新增Prompt模板";
  isEdit.value = false;
  Object.assign(formData, { id: 0, name: "", content: "", isDefault: false });
  dialogVisible.value = true;
};

const handleEdit = (row: PromptTemplate) => {
  if (!ensurePermission("btn:prompt-template:update", "权限不足，无法编辑Prompt模板")) {
    return;
  }
  dialogTitle.value = "编辑Prompt模板";
  isEdit.value = true;
  Object.assign(formData, {
    id: row.id,
    name: row.name,
    content: row.content,
    isDefault: row.isDefault
  });
  dialogVisible.value = true;
};

const handleDelete = async (row: PromptTemplate) => {
  if (!ensurePermission("btn:prompt-template:delete", "权限不足，无法删除Prompt模板")) {
    return;
  }
  try {
    await ElMessageBox.confirm(`确定删除模板“${row.name}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deletePromptTemplate(row.id);
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

const handleSetDefault = async (row: PromptTemplate) => {
  if (
    !ensurePermission("btn:prompt-template:set-default", "权限不足，无法设置默认模板")
  ) {
    return;
  }
  try {
    const res = await setDefaultPromptTemplate(row.id);
    if (res.code === 0) {
      ElMessage.success("设置默认成功");
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("设置默认失败");
  }
};

const handleSubmit = async () => {
  if (
    !ensurePermission(
      isEdit.value ? "btn:prompt-template:update" : "btn:prompt-template:create",
      isEdit.value ? "权限不足，无法保存Prompt模板" : "权限不足，无法新增Prompt模板"
    )
  ) {
    return;
  }
  if (!formData.name.trim()) {
    ElMessage.warning("请输入名称");
    return;
  }
  if (!formData.content.trim()) {
    ElMessage.warning("请输入内容");
    return;
  }

  const payload = {
    name: formData.name.trim(),
    content: formData.content,
    isDefault: formData.isDefault
  };

  try {
    const res = isEdit.value
      ? await updatePromptTemplate(formData.id, payload)
      : await createPromptTemplate(payload);

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
  <div class="page config-page">
    <div class="page-header">
      <div>
        <div class="page-title">Prompt 模板</div>
        <div class="page-subtitle">维护 LLM 提示词模板与版本</div>
      </div>
    </div>
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="名称/内容"
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
          <span>Prompt模板</span>
          <div class="flex gap-2">
            <el-button v-if="canLoadDefault" @click="handleLoadDefault">
              加载默认模板
            </el-button>
            <el-button v-if="canCreate" type="primary" @click="handleAdd">
              新增
            </el-button>
          </div>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="name" label="名称" min-width="200" />
        <el-table-column label="默认" width="90">
          <template #default="{ row }">
            <el-tag v-if="row.isDefault" type="success">默认</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="updatedAt" label="更新时间" width="180">
          <template #default="{ row }">
            {{
              new Date((row.updatedAt ?? row.createdAt) as string).toLocaleString()
            }}
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasOperationActions"
          label="操作"
          width="220"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button v-if="canUpdate" type="primary" link @click="handleEdit(row)">
              编辑
            </el-button>
            <el-button v-if="canDelete" type="danger" link @click="handleDelete(row)">
              删除
            </el-button>
            <el-button
              v-if="canSetDefault"
              type="success"
              link
              :disabled="row.isDefault"
              @click="handleSetDefault(row)"
            >
              设为默认
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

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="860">
      <el-form label-width="80px">
        <el-form-item label="名称" required>
          <el-input v-model="formData.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="内容" required>
          <el-input
            v-model="formData.content"
            type="textarea"
            :rows="14"
            placeholder="输入 Prompt 模板内容"
          />
        </el-form-item>
        <el-form-item label="设默认">
          <el-switch v-model="formData.isDefault" />
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

