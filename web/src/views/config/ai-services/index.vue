<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  AiServiceType,
  AiServicePurpose,
  createAiService,
  deleteAiService,
  getAiServiceById,
  getAiServiceList,
  getAiServiceModels,
  testAiServiceConnection,
  updateAiService,
  type AiServiceConfig,
  type AiServiceModelsResult,
  type CreateAiServiceRequest,
  type UpdateAiServiceRequest
} from "@/api/ai-service";

defineOptions({
  name: "AiServicesConfig"
});

const loading = ref(false);
const tableData = ref<AiServiceConfig[]>([]);
const showAllConfigs = ref(false);

const serviceTypeOptions = [
  { label: "OpenAI", value: AiServiceType.OpenAI },
  { label: "Azure OpenAI", value: AiServiceType.AzureOpenAI },
  { label: "Ollama", value: AiServiceType.Ollama },
  { label: "LM Studio", value: AiServiceType.LMStudio },
  { label: "OpenAI Compatible", value: AiServiceType.CustomOpenAICompatible }
];

const purposeOptions = [
  { label: "LLM", value: AiServicePurpose.Llm },
  { label: "Embedding", value: AiServicePurpose.Embedding }
];

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getAiServiceList({ page: 1, pageSize: 100 });
    if (res.code === 0) {
      tableData.value = res.data.items;
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载AI服务配置失败");
  } finally {
    loading.value = false;
  }
};

const dialogVisible = ref(false);
const dialogTitle = ref("");
const isEdit = ref(false);
const originalApiKey = ref("");
const modelsDialogVisible = ref(false);
const modelsLoading = ref(false);
const modelsInfo = reactive({
  id: 0,
  name: "",
  purpose: AiServicePurpose.Llm,
  llmModels: [] as string[],
  embeddingModels: [] as string[],
  message: ""
});

const formData = reactive({
  id: 0,
  name: "",
  serviceType: AiServiceType.Ollama,
  purpose: AiServicePurpose.Llm,
  priority: 0,
  endpoint: "",
  apiKey: "",
  embeddingModel: "",
  llmModel: ""
});

const hasPurpose = (value: number, flag: AiServicePurpose) => (value & flag) === flag;

const getServiceTypeLabel = (value: AiServiceType) =>
  serviceTypeOptions.find((x) => x.value === value)?.label || "-";

const formatValue = (value?: string | number | null) => {
  if (value === null || value === undefined || value === "") return "-";
  return String(value);
};

const pickConfigByPurpose = (purpose: AiServicePurpose) => {
  const exact = tableData.value.find((item) => item.purpose === purpose);
  if (exact) return exact;
  return tableData.value.find((item) => hasPurpose(item.purpose, purpose)) || null;
};

const llmConfig = computed(() => pickConfigByPurpose(AiServicePurpose.Llm));
const embeddingConfig = computed(() => pickConfigByPurpose(AiServicePurpose.Embedding));
const llmCount = computed(
  () => tableData.value.filter((item) => hasPurpose(item.purpose, AiServicePurpose.Llm)).length
);
const embeddingCount = computed(
  () => tableData.value.filter((item) => hasPurpose(item.purpose, AiServicePurpose.Embedding)).length
);

const normalizePurpose = (value: number) => {
  if (value === AiServicePurpose.Llm || value === AiServicePurpose.Embedding) return value;
  if (value === AiServicePurpose.None) return AiServicePurpose.Llm;
  return value;
};

const getDefaultPriority = (purpose: AiServicePurpose) => {
  const samePurpose = tableData.value
    .filter((item) => item.purpose === purpose)
    .map((item) => item.priority ?? 0);
  if (samePurpose.length === 0) return 0;
  return Math.max(...samePurpose) + 1;
};

watch(
  () => formData.purpose,
  (value) => {
    if (value === AiServicePurpose.Llm) {
      formData.embeddingModel = "";
    } else if (value === AiServicePurpose.Embedding) {
      formData.llmModel = "";
    }
  }
);

const handleAdd = (purpose: AiServicePurpose) => {
  dialogTitle.value = "新增AI服务配置";
  isEdit.value = false;
  originalApiKey.value = "";
  Object.assign(formData, {
    id: 0,
    name: "",
    serviceType: AiServiceType.Ollama,
    purpose,
    priority: getDefaultPriority(purpose),
    endpoint: "http://localhost:11434",
    apiKey: "",
    embeddingModel: purpose === AiServicePurpose.Embedding ? "nomic-embed-text" : "",
    llmModel: ""
  });
  dialogVisible.value = true;
};

const handleEdit = async (row: AiServiceConfig) => {
  dialogTitle.value = "编辑AI服务配置";
  isEdit.value = true;
  try {
    const res = await getAiServiceById(row.id);
    if (res.code === 0) {
      const detail = res.data;
      const rawPurpose = detail.purpose ?? AiServicePurpose.None;
      if (
        hasPurpose(rawPurpose, AiServicePurpose.Llm) &&
        hasPurpose(rawPurpose, AiServicePurpose.Embedding)
      ) {
        ElMessage.warning("检测到用途同时包含 LLM 与 Embedding，请重新选择单一用途");
      }
      originalApiKey.value = (detail.apiKey ?? "").trim();
      Object.assign(formData, {
        id: detail.id,
        name: detail.name,
        serviceType: detail.serviceType,
        purpose: normalizePurpose(rawPurpose),
        priority: detail.priority ?? 0,
        endpoint: detail.endpoint ?? "",
        apiKey: detail.apiKey ?? "",
        embeddingModel: detail.embeddingModel ?? "",
        llmModel: detail.llmModel ?? ""
      });
    } else {
      ElMessage.error(res.message || "加载配置失败");
      return;
    }
  } catch {
    ElMessage.error("加载配置失败");
    return;
  }
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

const handleTest = async (row: AiServiceConfig) => {
  try {
    const res = await testAiServiceConnection(row.id);
    if (res.code === 0) {
      const r = res.data;
      const message = `${r.success ? "成功" : "失败"}：${r.message}（${r.elapsedMs}ms${
        r.httpStatusCode ? `, HTTP ${r.httpStatusCode}` : ""
      }）`;
      if (r.success && r.httpStatusCode && r.httpStatusCode >= 200 && r.httpStatusCode < 400) {
        ElMessage.success(message);
      } else if (r.success) {
        ElMessage.warning(message);
      } else {
        ElMessage.error(message);
      }
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("连接测试失败");
  }
};

const handleProbeModels = async (row: AiServiceConfig) => {
  modelsInfo.id = row.id;
  modelsInfo.name = row.name;
  modelsInfo.purpose = row.purpose;
  modelsInfo.llmModels = [];
  modelsInfo.embeddingModels = [];
  modelsInfo.message = "";
  modelsDialogVisible.value = true;
  await loadModels();
};

const loadModels = async () => {
  if (!modelsInfo.id) return;
  modelsLoading.value = true;
  try {
    const res = await getAiServiceModels(modelsInfo.id);
    if (res.code === 0) {
      const data = res.data as AiServiceModelsResult;
      modelsInfo.llmModels = data.llmModels || [];
      modelsInfo.embeddingModels = data.embeddingModels || [];
      modelsInfo.message = data.message || "";
    } else {
      modelsInfo.message = res.message || "模型探测失败";
    }
  } catch {
    modelsInfo.message = "模型探测失败";
  } finally {
    modelsLoading.value = false;
  }
};

const copyModelName = async (name: string) => {
  if (!name) return;
  try {
    await navigator.clipboard.writeText(name);
    ElMessage.success("已复制模型名称");
  } catch {
    try {
      const textarea = document.createElement("textarea");
      textarea.value = name;
      textarea.style.position = "fixed";
      textarea.style.opacity = "0";
      document.body.appendChild(textarea);
      textarea.focus();
      textarea.select();
      const ok = document.execCommand("copy");
      document.body.removeChild(textarea);
      if (ok) {
        ElMessage.success("已复制模型名称");
      } else {
        ElMessage.error("复制失败，请手动复制");
      }
    } catch {
      ElMessage.error("复制失败，请手动复制");
    }
  }
};

const formatPurpose = (purpose: number) => {
  const labels: string[] = [];
  if (hasPurpose(purpose, AiServicePurpose.Llm)) labels.push("LLM");
  if (hasPurpose(purpose, AiServicePurpose.Embedding)) labels.push("Embedding");
  return labels.length ? labels.join(" / ") : "-";
};

const handleSubmit = async () => {
  if (!formData.name.trim()) {
    ElMessage.warning("请输入名称");
    return;
  }
  if (!formData.purpose) {
    ElMessage.warning("请至少选择一个用途");
    return;
  }
  if (formData.purpose !== AiServicePurpose.Llm && formData.purpose !== AiServicePurpose.Embedding) {
    ElMessage.warning("用途只能选择一个（LLM 或 Embedding）");
    return;
  }
  if (formData.purpose === AiServicePurpose.Llm && !formData.llmModel.trim()) {
    ElMessage.warning("请输入 LLM 模型");
    return;
  }
  if (formData.purpose === AiServicePurpose.Embedding && !formData.embeddingModel.trim()) {
    ElMessage.warning("请输入 Embedding 模型");
    return;
  }

  const apiKey = formData.apiKey.trim();
  const embeddingModel = formData.embeddingModel?.trim() || null;
  const llmModel = formData.llmModel?.trim() || null;
  const basePayload: CreateAiServiceRequest = {
    name: formData.name.trim(),
    serviceType: formData.serviceType,
    purpose: formData.purpose,
    priority: formData.priority,
    endpoint: formData.endpoint?.trim() || null,
    embeddingModel,
    llmModel
  };
  if (formData.purpose === AiServicePurpose.Llm) {
    basePayload.embeddingModel = null;
  }
  if (formData.purpose === AiServicePurpose.Embedding) {
    basePayload.llmModel = null;
  }

  try {
    const res = await (async () => {
      if (isEdit.value) {
        const updatePayload: UpdateAiServiceRequest = { ...basePayload };
        if (apiKey !== originalApiKey.value) {
          updatePayload.apiKey = apiKey; // 允许清空
        }
        return updateAiService(formData.id, updatePayload);
      }

      const createPayload: CreateAiServiceRequest = {
        ...basePayload,
        apiKey: apiKey || ""
      };
      return createAiService(createPayload);
    })();

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

onMounted(loadData);
</script>

<template>
  <div class="page config-page">
    <div class="page-header">
      <div>
        <div class="page-title">AI 服务配置</div>
        <div class="page-subtitle">管理 LLM 与 Embedding 服务连接</div>
      </div>
    </div>
    <el-alert
      v-if="llmCount > 1 || embeddingCount > 1"
      type="warning"
      show-icon
      class="config-alert"
    >
      <template #default>
        检测到多个 LLM/Embedding 配置，页面默认仅展示优先级最高的一条。
        <el-button type="primary" link @click="showAllConfigs = true">查看全部</el-button>
      </template>
    </el-alert>

    <div class="service-grid">
      <el-card class="service-card" v-loading="loading">
        <template #header>
          <div class="card-header">
            <span>LLM 服务</span>
            <div class="card-actions">
              <el-button type="primary" @click="handleAdd(AiServicePurpose.Llm)">新增</el-button>
              <template v-if="llmConfig">
                <el-button type="primary" link @click="handleEdit(llmConfig)">编辑</el-button>
                <el-button type="danger" link @click="handleDelete(llmConfig)">删除</el-button>
                <el-button type="warning" link @click="handleTest(llmConfig)">测试</el-button>
                <el-button type="success" link @click="handleProbeModels(llmConfig)">模型</el-button>
              </template>
            </div>
          </div>
        </template>
        <el-empty v-if="!llmConfig" description="未配置 LLM 服务" />
        <div v-else class="config-grid">
          <div class="config-row">
            <div class="config-label">名称</div>
            <div class="config-value">{{ formatValue(llmConfig.name) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">类型</div>
            <div class="config-value">{{ getServiceTypeLabel(llmConfig.serviceType) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">优先级</div>
            <div class="config-value">{{ llmConfig.priority }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">Endpoint</div>
            <div class="config-value">{{ formatValue(llmConfig.endpoint) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">LLM 模型</div>
            <div class="config-value">{{ formatValue(llmConfig.llmModel) }}</div>
          </div>
        </div>
      </el-card>

      <el-card class="service-card" v-loading="loading">
        <template #header>
          <div class="card-header">
            <span>Embedding 服务</span>
            <div class="card-actions">
              <el-button type="primary" @click="handleAdd(AiServicePurpose.Embedding)">新增</el-button>
              <template v-if="embeddingConfig">
                <el-button type="primary" link @click="handleEdit(embeddingConfig)">编辑</el-button>
                <el-button type="danger" link @click="handleDelete(embeddingConfig)">删除</el-button>
                <el-button type="warning" link @click="handleTest(embeddingConfig)">测试</el-button>
                <el-button type="success" link @click="handleProbeModels(embeddingConfig)">模型</el-button>
              </template>
            </div>
          </div>
        </template>
        <el-empty v-if="!embeddingConfig" description="未配置 Embedding 服务" />
        <div v-else class="config-grid">
          <div class="config-row">
            <div class="config-label">名称</div>
            <div class="config-value">{{ formatValue(embeddingConfig.name) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">类型</div>
            <div class="config-value">{{ getServiceTypeLabel(embeddingConfig.serviceType) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">优先级</div>
            <div class="config-value">{{ embeddingConfig.priority }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">Endpoint</div>
            <div class="config-value">{{ formatValue(embeddingConfig.endpoint) }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">Embedding 模型</div>
            <div class="config-value">{{ formatValue(embeddingConfig.embeddingModel) }}</div>
          </div>
        </div>
      </el-card>
    </div>

    <el-card v-if="showAllConfigs" class="service-table">
      <template #header>
        <div class="flex justify-between items-center">
          <span>全部配置</span>
          <el-button @click="showAllConfigs = false">收起</el-button>
        </div>
      </template>
      <el-table :data="tableData" stripe>
        <el-table-column prop="id" label="ID" width="80" />
        <el-table-column prop="name" label="名称" min-width="180" />
        <el-table-column prop="serviceType" label="类型" width="160">
          <template #default="{ row }">
            {{ getServiceTypeLabel(row.serviceType) }}
          </template>
        </el-table-column>
        <el-table-column prop="purpose" label="用途" width="160">
          <template #default="{ row }">
            {{ formatPurpose(row.purpose) }}
          </template>
        </el-table-column>
        <el-table-column prop="endpoint" label="Endpoint" min-width="240" />
        <el-table-column prop="embeddingModel" label="EmbeddingModel" min-width="160" />
        <el-table-column prop="llmModel" label="LLMModel" min-width="160" />
        <el-table-column label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="handleEdit(row)">编辑</el-button>
            <el-button type="danger" link @click="handleDelete(row)">删除</el-button>
            <el-button type="warning" link @click="handleTest(row)">测试</el-button>
            <el-button type="success" link @click="handleProbeModels(row)">模型</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="620">
      <el-form label-width="120px">
        <el-form-item label="名称" required>
          <el-input v-model="formData.name" maxlength="100" />
        </el-form-item>
        <el-form-item label="类型" required>
          <el-select
            v-model="formData.serviceType"
            class="w-full"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="opt in serviceTypeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="用途" required>
          <el-radio-group v-model="formData.purpose">
            <el-radio
              v-for="opt in purposeOptions"
              :key="opt.value"
              :label="opt.value"
            >
              {{ opt.label }}
            </el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number
            v-model="formData.priority"
            :min="0"
            :max="9999"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item label="Endpoint">
          <el-input
            v-model="formData.endpoint"
            placeholder="例如 http://localhost:11434 或 https://api.moonshot.cn（不包含 /v1）"
          />
        </el-form-item>
        <el-form-item label="ApiKey">
          <el-input
            v-model="formData.apiKey"
            type="password"
            show-password
            placeholder="可查看/修改（编辑时）"
          />
        </el-form-item>
        <el-form-item
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Embedding)"
          label="EmbeddingModel"
          required
        >
          <el-input v-model="formData.embeddingModel" />
        </el-form-item>
        <el-form-item
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Llm)"
          label="LLMModel"
          required
        >
          <el-input v-model="formData.llmModel" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleSubmit">确定</el-button>
      </template>
    </el-dialog>

    <el-dialog v-model="modelsDialogVisible" title="远端模型探测" width="520px">
      <div v-loading="modelsLoading">
        <div class="model-title">
          {{ modelsInfo.name || "AI服务" }}
        </div>
        <div v-if="modelsInfo.message" class="model-message">
          {{ modelsInfo.message }}
        </div>
        <div v-if="hasPurpose(modelsInfo.purpose, AiServicePurpose.Llm)" class="model-section">
          <div class="model-label">LLM 模型</div>
          <div class="model-tags" v-if="modelsInfo.llmModels.length">
            <el-tag
              v-for="m in modelsInfo.llmModels"
              :key="m"
              size="small"
              class="model-tag"
              :title="`点击复制 ${m}`"
              @click="copyModelName(m)"
            >
              {{ m }}
            </el-tag>
          </div>
          <div v-else class="model-empty">未返回 LLM 模型</div>
        </div>
        <div v-if="hasPurpose(modelsInfo.purpose, AiServicePurpose.Embedding)" class="model-section">
          <div class="model-label">Embedding 模型</div>
          <div class="model-tags" v-if="modelsInfo.embeddingModels.length">
            <el-tag
              v-for="m in modelsInfo.embeddingModels"
              :key="m"
              size="small"
              type="info"
              class="model-tag"
              :title="`点击复制 ${m}`"
              @click="copyModelName(m)"
            >
              {{ m }}
            </el-tag>
          </div>
          <div v-else class="model-empty">未返回 Embedding 模型</div>
        </div>
      </div>
      <template #footer>
        <el-button @click="modelsDialogVisible = false">关闭</el-button>
        <el-button type="primary" @click="loadModels">重新探测</el-button>
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

.config-alert {
  margin-bottom: 8px;
}

.service-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
}

.service-card {
  min-height: 220px;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.card-actions {
  display: flex;
  align-items: center;
  gap: 4px;
}

.config-grid {
  display: grid;
  grid-template-columns: 1fr;
  gap: 10px;
}

.config-row {
  display: grid;
  grid-template-columns: 110px 1fr;
  gap: 12px;
  align-items: start;
}

.config-label {
  font-size: 12px;
  color: #6b7280;
}

.config-value {
  font-size: 13px;
  color: var(--color-text);
  word-break: break-all;
}

.service-table {
  margin-top: 8px;
}

.model-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 8px;
}

.model-message {
  font-size: 12px;
  color: #6b7280;
  margin-bottom: 12px;
}

.model-section {
  margin-bottom: 12px;
}

.model-label {
  font-size: 12px;
  color: #6b7280;
  margin-bottom: 6px;
}

.model-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.model-tag {
  cursor: pointer;
}

.model-tag:hover {
  opacity: 0.85;
}

.model-empty {
  font-size: 12px;
  color: #c0c4cc;
}

</style>

