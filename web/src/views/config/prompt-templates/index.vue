<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import {
  ElMessage,
  ElMessageBox,
  type FormInstance,
  type FormRules
} from "element-plus";
import {
  getPromptTemplateList,
  previewPromptTemplate,
  resetSystemPromptTemplate,
  updatePromptTemplate,
  type PromptTemplate,
  type PromptTemplatePreviewResponse
} from "@/api/prompt-template";
import { hasPerms } from "@/utils/auth";
import { formatApiUtcDateTime } from "@/utils/date-time";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { isMessageBoxCancel } from "@/utils/message-box";
import { ensurePermission } from "@/utils/permission-guard";
import { requiredTrimmedRule, validateForm } from "@/utils/form-rules";

defineOptions({
  name: "PromptTemplates"
});

const loading = ref(false);
const previewLoading = ref(false);
const tableData = ref<PromptTemplate[]>([]);
const total = ref(0);
const previewResult = ref<PromptTemplatePreviewResponse | null>(null);

const queryParams = reactive({
  page: 1,
  pageSize: 50,
  keyword: ""
});

const canUpdate = computed(() => hasPerms("btn:prompt-template:update"));
const canPreview = computed(() => hasPerms("btn:prompt-template:preview"));
const canResetSystem = computed(() =>
  hasPerms("btn:prompt-template:reset-system")
);
const hasOperationActions = computed(
  () => canUpdate.value || canResetSystem.value
);

const dialogVisible = ref(false);
const dialogTitle = ref("");
const formData = reactive({
  id: 0,
  name: "",
  scene: "",
  displayName: "",
  content: "",
  usageDescription: "",
  availableVariables: [] as string[]
});
const formRef = ref<FormInstance>();
const formRules: FormRules<typeof formData> = {
  displayName: [requiredTrimmedRule("请输入显示名称")],
  content: [requiredTrimmedRule("请输入内容")]
};

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

const openEditDialog = (row: PromptTemplate) => {
  dialogTitle.value = `编辑模板 - ${row.displayName}`;
  previewResult.value = null;
  Object.assign(formData, {
    id: row.id,
    name: row.name,
    scene: row.scene,
    displayName: row.displayName,
    content: row.content,
    usageDescription: row.usageDescription,
    availableVariables: [...row.availableVariables]
  });
  dialogVisible.value = true;
};

const handleEdit = (row: PromptTemplate) => {
  if (
    !ensurePermission(
      "btn:prompt-template:update",
      "权限不足，无法编辑Prompt模板"
    )
  ) {
    return;
  }

  openEditDialog(row);
};

const handlePreview = async () => {
  if (
    !ensurePermission(
      "btn:prompt-template:preview",
      "权限不足，无法预览Prompt模板"
    )
  ) {
    return;
  }
  const contentValid = Boolean(
    await formRef.value?.validateField("content").catch(() => false)
  );
  if (!contentValid) return;

  previewLoading.value = true;
  try {
    const res = await previewPromptTemplate({
      scene: formData.scene,
      content: formData.content
    });
    if (res.code === 0) {
      previewResult.value = res.data;
      ElMessage.success(res.data.isValid ? "预览通过" : "预览存在校验问题");
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("预览失败");
  } finally {
    previewLoading.value = false;
  }
};

const handleSubmit = async () => {
  if (
    !ensurePermission(
      "btn:prompt-template:update",
      "权限不足，无法保存Prompt模板"
    )
  ) {
    return;
  }
  if (!(await validateForm(formRef.value))) return;

  try {
    const res = await updatePromptTemplate(formData.id, {
      displayName: formData.displayName.trim(),
      content: formData.content
    });

    if (res.code === 0) {
      ElMessage.success("更新成功");
      dialogVisible.value = false;
      previewResult.value = null;
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("保存失败");
  }
};

const handleResetSystem = async (row: PromptTemplate) => {
  if (
    !ensurePermission(
      "btn:prompt-template:reset-system",
      "权限不足，无法恢复系统默认模板"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      `确定将“${row.displayName}”恢复为系统默认内容吗？`,
      "提示",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );

    const res = await resetSystemPromptTemplate(row.scene);
    if (res.code === 0) {
      ElMessage.success("已恢复系统默认内容");
      loadData();

      if (dialogVisible.value && formData.id === row.id) {
        openEditDialog(res.data);
      }
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    if (isMessageBoxCancel(error) || isGloballyHandledAuthError(error)) return;
    ElMessage.error(getRequestErrorMessage(error, "恢复系统默认模板失败"));
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
    <el-card class="full-height-table-wrapper">
      <template #header>
        <div class="list-card-toolbar">
          <div class="list-card-toolbar__right">
            <span class="text-sm text-gray-500">
              页面只维护运行时实际使用的系统模板，不再提供设默认和任意新增。
            </span>
            <el-form :inline="true" class="filter-form">
              <el-form-item label="关键词">
                <el-input
                  v-model="queryParams.keyword"
                  placeholder="系统键 / 显示名称 / 内容"
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

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="name" label="系统键" min-width="180" />
        <el-table-column prop="displayName" label="显示名称" min-width="160" />
        <el-table-column label="系统模板" width="100">
          <template #default="{ row }">
            <el-tag v-if="row.isSystem" type="success">系统</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column prop="usageDescription" label="用途" min-width="220" />
        <el-table-column label="占位符" min-width="280">
          <template #default="{ row }">
            <div class="tag-list">
              <el-tag
                v-for="variable in row.availableVariables"
                :key="variable"
                size="small"
                type="info"
              >
                {{ variable }}
              </el-tag>
            </div>
          </template>
        </el-table-column>
        <el-table-column prop="updatedAt" label="更新时间" width="180">
          <template #default="{ row }">
            {{ formatApiUtcDateTime(row.updatedAt ?? row.createdAt) }}
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasOperationActions"
          label="操作"
          width="180"
          fixed="right"
        >
          <template #default="{ row }">
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="handleEdit(row)"
            >
              编辑
            </el-button>
            <el-button
              v-if="canResetSystem"
              type="warning"
              link
              @click="handleResetSystem(row)"
            >
              恢复默认
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

    <el-dialog
      v-model="dialogVisible"
      :title="dialogTitle"
      width="min(960px, calc(100vw - 32px))"
    >
      <el-form
        ref="formRef"
        :model="formData"
        :rules="formRules"
        label-width="100px"
        status-icon
      >
        <el-form-item label="系统键">
          <el-input :model-value="formData.name" readonly />
        </el-form-item>
        <el-form-item label="用途说明">
          <el-input :model-value="formData.usageDescription" readonly />
        </el-form-item>
        <el-form-item label="显示名称" prop="displayName">
          <el-input v-model="formData.displayName" maxlength="100" />
        </el-form-item>
        <el-form-item label="占位符">
          <div class="tag-list">
            <el-tag
              v-for="variable in formData.availableVariables"
              :key="variable"
              size="small"
              type="info"
            >
              {{ variable }}
            </el-tag>
          </div>
        </el-form-item>
        <el-form-item label="内容" prop="content">
          <el-input
            v-model="formData.content"
            type="textarea"
            :rows="16"
            placeholder="输入 Prompt 模板内容"
          />
        </el-form-item>
      </el-form>

      <el-divider content-position="left">预览测试</el-divider>
      <div v-if="canPreview" class="mb-4">
        <el-button :loading="previewLoading" @click="handlePreview">
          执行预览
        </el-button>
      </div>

      <el-card v-if="previewResult" shadow="never" class="preview-card">
        <el-alert
          :title="previewResult.isValid ? '预览校验通过' : '预览存在问题'"
          :type="previewResult.isValid ? 'success' : 'warning'"
          :closable="false"
          class="mb-4"
        />

        <div v-if="previewResult.errors.length" class="mb-4">
          <div class="preview-title">校验问题</div>
          <ul class="preview-errors">
            <li v-for="error in previewResult.errors" :key="error">
              {{ error }}
            </li>
          </ul>
        </div>

        <div class="mb-4">
          <div class="preview-title">渲染结果</div>
          <pre class="preview-block">{{ previewResult.renderedPrompt }}</pre>
        </div>

        <div class="mb-4">
          <div class="preview-title">JSON 示例</div>
          <pre class="preview-block">{{
            previewResult.exampleJson || "未提取到 JSON 示例"
          }}</pre>
        </div>

        <div>
          <div class="preview-title">结构化校验</div>
          <el-tag
            :type="previewResult.structuredOutputIsValid ? 'success' : 'danger'"
          >
            {{ previewResult.structuredOutputIsValid ? "通过" : "失败" }}
          </el-tag>
          <div
            v-if="previewResult.structuredOutputError"
            class="mt-2 text-sm text-red-500"
          >
            {{ previewResult.structuredOutputError }}
          </div>
        </div>
      </el-card>

      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canUpdate" type="primary" @click="handleSubmit">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 0;
}

.tag-list {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.preview-card {
  background: var(--app-bg-page);
}

.preview-title {
  margin-bottom: 8px;
  font-size: 13px;
  font-weight: 600;
  color: var(--app-text-secondary);
}

.preview-block {
  padding: 12px;
  margin: 0;
  font-size: 12px;
  line-height: 1.6;
  color: var(--app-bg-card);
  word-break: break-word;
  white-space: pre-wrap;
  background: var(--app-text-primary);
  border-radius: 8px;
}

.preview-errors {
  padding-left: 18px;
  margin: 0;
  color: var(--app-danger);
}
</style>
