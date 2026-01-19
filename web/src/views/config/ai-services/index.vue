<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  AiServiceType,
  createAiService,
  deleteAiService,
  getAiServiceList,
  setDefaultAiService,
  testAiServiceConnection,
  updateAiService,
  type AiServiceConfig
} from "@/api/ai-service";

defineOptions({
  name: "AiServicesConfig"
});

const loading = ref(false);
const tableData = ref<AiServiceConfig[]>([]);
const total = ref(0);

const queryParams = reactive({
  page: 1,
  pageSize: 20,
  keyword: "",
  serviceType: undefined as AiServiceType | undefined
});

const serviceTypeOptions = [
  { label: "OpenAI", value: AiServiceType.OpenAI },
  { label: "Azure OpenAI", value: AiServiceType.AzureOpenAI },
  { label: "Ollama", value: AiServiceType.Ollama },
  { label: "LM Studio", value: AiServiceType.LMStudio },
  { label: "OpenAI Compatible", value: AiServiceType.CustomOpenAICompatible }
];

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getAiServiceList(queryParams);
    if (res.code === 0) {
      tableData.value = res.data.items;
      total.value = res.data.total;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载AI服务配置失败");
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
  queryParams.serviceType = undefined;
  queryParams.page = 1;
  loadData();
};

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);

const formData = reactive({
  id: 0,
  name: "",
  serviceType: AiServiceType.Ollama,
  endpoint: "",
  apiKey: "",
  embeddingModel: "",
  llmModel: "",
  isDefault: false
});

const handleAdd = () => {
  dialogTitle.value = "新增AI服务配置";
  isEdit.value = false;
  Object.assign(formData, {
    id: 0,
    name: "",
    serviceType: AiServiceType.Ollama,
    endpoint: "http://localhost:11434",
    apiKey: "",
    embeddingModel: "nomic-embed-text",
    llmModel: "",
    isDefault: false
  });
  dialogVisible.value = true;
};

const handleEdit = (row: AiServiceConfig) => {
  dialogTitle.value = "编辑AI服务配置";
  isEdit.value = true;
  Object.assign(formData, {
    id: row.id,
    name: row.name,
    serviceType: row.serviceType,
    endpoint: row.endpoint ?? "",
    apiKey: "",
    embeddingModel: row.embeddingModel ?? "",
    llmModel: row.llmModel ?? "",
    isDefault: row.isDefault
  });
  dialogVisible.value = true;
};

const handleDelete = async (row: AiServiceConfig) => {
  try {
    await ElMessageBox.confirm(`确定删除配置“${row.name}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteAiService(row.id);
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

const handleSetDefault = async (row: AiServiceConfig) => {
  try {
    const res = await setDefaultAiService(row.id);
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

const handleTest = async (row: AiServiceConfig) => {
  try {
    const res = await testAiServiceConnection(row.id);
    if (res.code === 0) {
      const r = res.data;
      ElMessage.info(
        `${r.success ? "成功" : "失败"}：${r.message}（${r.elapsedMs}ms${
          r.httpStatusCode ? `, HTTP ${r.httpStatusCode}` : ""
        }）`
      );
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("连接测试失败");
  }
};

const handleSubmit = async () => {
  if (!formData.name.trim()) {
    ElMessage.warning("请输入名称");
    return;
  }

  const payload = {
    name: formData.name.trim(),
    serviceType: formData.serviceType,
    endpoint: formData.endpoint?.trim() || null,
    apiKey: formData.apiKey, // 允许空字符串清空
    embeddingModel: formData.embeddingModel?.trim() || null,
    llmModel: formData.llmModel?.trim() || null,
    isDefault: formData.isDefault
  };

  try {
    const res = isEdit.value
      ? await updateAiService(formData.id, payload)
      : await createAiService(payload);

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
  <div class="main">
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="关键词">
          <el-input
            v-model="queryParams.keyword"
            placeholder="名称/Endpoint"
            clearable
            @keyup.enter="handleSearch"
          />
        </el-form-item>
        <el-form-item label="类型">
          <el-select v-model="queryParams.serviceType" clearable placeholder="全部">
            <el-option
              v-for="opt in serviceTypeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
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
          <span>AI服务配置</span>
          <el-button type="primary" @click="handleAdd">新增</el-button>
        </div>
      </template>

      <el-table v-loading="loading" :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column prop="serviceType" label="类型" width="160">
          <template #default="{ row }">
            {{ serviceTypeOptions.find(x => x.value === row.serviceType)?.label }}
          </template>
        </el-table-column>
        <el-table-column prop="endpoint" label="Endpoint" min-width="240" />
        <el-table-column prop="embeddingModel" label="EmbeddingModel" min-width="160" />
        <el-table-column prop="llmModel" label="LLMModel" min-width="160" />
        <el-table-column label="默认" width="90">
          <template #default="{ row }">
            <el-tag v-if="row.isDefault" type="success">默认</el-tag>
            <span v-else>-</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="250" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="handleEdit(row)">编辑</el-button>
            <el-button type="danger" link @click="handleDelete(row)">删除</el-button>
            <el-button type="warning" link @click="handleTest(row)">测试</el-button>
            <el-button
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

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="620">
      <el-form label-width="120px">
        <el-form-item label="名称" required>
          <el-input v-model="formData.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="类型" required>
          <el-select v-model="formData.serviceType" class="w-full">
            <el-option
              v-for="opt in serviceTypeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="Endpoint">
          <el-input v-model="formData.endpoint" placeholder="例如 http://localhost:11434 或 https://api.openai.com/v1" />
        </el-form-item>
        <el-form-item label="ApiKey">
          <el-input
            v-model="formData.apiKey"
            type="password"
            show-password
            placeholder="留空表示不修改（编辑时）；提交空字符串表示清空"
          />
        </el-form-item>
        <el-form-item label="EmbeddingModel">
          <el-input v-model="formData.embeddingModel" />
        </el-form-item>
        <el-form-item label="LLMModel">
          <el-input v-model="formData.llmModel" />
        </el-form-item>
        <el-form-item label="设为默认">
          <el-switch v-model="formData.isDefault" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.main {
  padding: 20px;
}
</style>

