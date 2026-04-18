<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from "vue";
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
  type AiServiceTestResult,
  updateAiService,
  type AiServiceConfig,
  type AiServiceModelsResult,
  type CreateAiServiceRequest,
  type UpdateAiServiceRequest
} from "@/api/ai-service";
import {
  DEFAULT_RECALL_TOP_K,
  MAX_RECALL_TOP_K
} from "@/api/matching";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "AiServicesConfig"
});

const loading = ref(false);
const tableData = ref<AiServiceConfig[]>([]);
const showAllConfigs = ref(false);
const testingState = reactive<Record<string, boolean>>({});
const probingState = reactive<Record<string, boolean>>({});
const expandedTestRowKeys = ref<string[]>([]);
const TEST_ACTION_LABEL = "完整测试";

type TestResultTagType = "success" | "danger" | "warning" | "info";
type TestResultCategory =
  | "success"
  | "auth"
  | "endpoint"
  | "rate-limit"
  | "timeout"
  | "remote"
  | "general";

interface TestResultTag {
  label: string;
  type: TestResultTagType;
}

interface TestResultDetail {
  label: string;
  value: string;
}

interface InlineTestResultCard {
  rowId: number;
  rowName: string;
  success: boolean;
  category: TestResultCategory;
  statusText: string;
  summary: string;
  message: string;
  tags: TestResultTag[];
  details: TestResultDetail[];
}

const activeTestResult = ref<InlineTestResultCard | null>(null);

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

const canCreate = computed(() => hasPerms("btn:ai-service:create"));
const canUpdate = computed(() => hasPerms("btn:ai-service:update"));
const canDelete = computed(() => hasPerms("btn:ai-service:delete"));
const canTest = computed(() => hasPerms("btn:ai-service:test"));
const canProbeModels = computed(() => hasPerms("btn:ai-service:models"));
const canSubmit = computed(() =>
  isEdit.value ? canUpdate.value : canCreate.value
);
const hasActionButtons = computed(
  () =>
    canUpdate.value || canDelete.value || canTest.value || canProbeModels.value
);
const hasSummaryActionButtons = computed(
  () => canUpdate.value || canDelete.value || canProbeModels.value
);

const loadData = async () => {
  loading.value = true;
  try {
    const res = await getAiServiceList({ page: 1, pageSize: 100 });
    if (res.code === 0) {
      tableData.value = res.data.items;
      if (
        activeTestResult.value &&
        !res.data.items.some(item => item.id === activeTestResult.value?.rowId)
      ) {
        clearInlineTestResult();
      }
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
  llmModel: "",
  disableThinking: false,
  defaultRecallTopK: DEFAULT_RECALL_TOP_K
});

const hasPurpose = (value: number, flag: AiServicePurpose) =>
  (value & flag) === flag;

const setRowLoading = (
  state: Record<string, boolean>,
  id: string | number,
  value: boolean
) => {
  if (id === null || id === undefined || id === "") return;
  state[String(id)] = value;
};

const isRowLoading = (
  state: Record<string, boolean>,
  id?: string | number | null
) => {
  if (id === null || id === undefined || id === "") return false;
  return !!state[String(id)];
};

const buildTestResultTags = (
  success: boolean,
  category: TestResultCategory
): TestResultTag[] => {
  const tags: TestResultTag[] = [
    {
      label: TEST_ACTION_LABEL,
      type: "info"
    },
    {
      label: success ? "成功" : "失败",
      type: success ? "success" : "danger"
    }
  ];

  if (success) {
    tags.push({
      label: "连接正常",
      type: "success"
    });
    return tags;
  }

  const categoryTagMap: Record<TestResultCategory, TestResultTag | null> = {
    success: null,
    auth: { label: "ApiKey", type: "danger" },
    endpoint: { label: "Endpoint", type: "warning" },
    "rate-limit": { label: "限流", type: "warning" },
    timeout: { label: "超时", type: "warning" },
    remote: { label: "远端服务", type: "info" },
    general: { label: "连接异常", type: "info" }
  };

  const categoryTag = categoryTagMap[category];
  if (categoryTag) {
    tags.push(categoryTag);
  }

  return tags;
};

const inferTestResultCategory = (
  success: boolean,
  message: string
): TestResultCategory => {
  if (success) return "success";

  if (
    message.includes("鉴权失败") ||
    message.includes("ApiKey") ||
    message.toLowerCase().includes("invalid authentication") ||
    message.toLowerCase().includes("invalid token")
  ) {
    return "auth";
  }

  if (message.includes("Endpoint") || message.includes("地址无效")) {
    return "endpoint";
  }

  if (
    message.includes("限流") ||
    message.includes("额度受限") ||
    message.includes("429")
  ) {
    return "rate-limit";
  }

  if (message.includes("超时")) {
    return "timeout";
  }

  if (message.includes("远端接口服务异常") || message.includes("HTTP 5")) {
    return "remote";
  }

  return "general";
};

const buildTestResultDetails = (
  row: AiServiceConfig,
  result: Pick<
    AiServiceTestResult,
    | "elapsedMs"
    | "serviceElapsedMs"
    | "targetModel"
    | "targetEndpoint"
    | "hostPort"
    | "httpStatusCode"
  >
): TestResultDetail[] => {
  const details: TestResultDetail[] = [
    { label: "服务", value: row.name },
    { label: "总耗时", value: `${result.elapsedMs}ms` }
  ];

  if (typeof result.serviceElapsedMs === "number") {
    details.push({ label: "接口耗时", value: `${result.serviceElapsedMs}ms` });
  }
  if (result.targetModel) {
    details.push({ label: "模型", value: result.targetModel });
  }
  if (result.targetEndpoint) {
    details.push({ label: "Endpoint", value: result.targetEndpoint });
  }
  if (result.hostPort) {
    details.push({ label: "宿主", value: result.hostPort });
  }
  if (result.httpStatusCode) {
    details.push({ label: "HTTP", value: String(result.httpStatusCode) });
  }

  return details;
};

const buildInlineTestResultCard = (
  row: AiServiceConfig,
  result: AiServiceTestResult
): InlineTestResultCard => {
  const category = inferTestResultCategory(
    result.success,
    result.message || ""
  );

  return {
    rowId: row.id,
    rowName: row.name,
    success: result.success,
    category,
    statusText: result.success ? "连接正常" : "需要处理",
    summary: `${TEST_ACTION_LABEL}${result.success ? "成功" : "失败"}`,
    message: result.message || (result.success ? "测试通过" : "连接测试失败"),
    tags: buildTestResultTags(result.success, category),
    details: buildTestResultDetails(row, result)
  };
};

const showInlineTestResult = async (card: InlineTestResultCard) => {
  activeTestResult.value = card;
  showAllConfigs.value = true;
  expandedTestRowKeys.value = [String(card.rowId)];

  await nextTick();
  document
    .getElementById(`ai-test-result-${card.rowId}`)
    ?.scrollIntoView({ behavior: "smooth", block: "nearest" });
};

const clearInlineTestResult = () => {
  activeTestResult.value = null;
  expandedTestRowKeys.value = [];
};

const getTestResultCardClass = (
  category: TestResultCategory,
  success: boolean
) => {
  if (success) return "ai-test-result-card--success";

  const classMap: Record<TestResultCategory, string> = {
    success: "ai-test-result-card--success",
    auth: "ai-test-result-card--auth",
    endpoint: "ai-test-result-card--endpoint",
    "rate-limit": "ai-test-result-card--rate-limit",
    timeout: "ai-test-result-card--timeout",
    remote: "ai-test-result-card--remote",
    general: "ai-test-result-card--general"
  };

  return classMap[category];
};

const extractErrorMessage = (error: unknown, fallback: string) => {
  const responseMessage = (error as any)?.response?.data?.message;
  if (typeof responseMessage === "string" && responseMessage.trim()) {
    return responseMessage.trim();
  }

  const errorMessage = (error as any)?.message;
  if (typeof errorMessage === "string" && errorMessage.trim()) {
    return errorMessage.trim();
  }

  return fallback;
};

const getServiceTypeLabel = (value: AiServiceType) =>
  serviceTypeOptions.find(x => x.value === value)?.label || "-";

const formatValue = (value?: string | number | null) => {
  if (value === null || value === undefined || value === "") return "-";
  return String(value);
};

const pickConfigByPurpose = (purpose: AiServicePurpose) => {
  const exact = tableData.value.find(item => item.purpose === purpose);
  if (exact) return exact;
  return (
    tableData.value.find(item => hasPurpose(item.purpose, purpose)) || null
  );
};

const llmConfig = computed(() => pickConfigByPurpose(AiServicePurpose.Llm));
const embeddingConfig = computed(() =>
  pickConfigByPurpose(AiServicePurpose.Embedding)
);
const llmCount = computed(
  () =>
    tableData.value.filter(item =>
      hasPurpose(item.purpose, AiServicePurpose.Llm)
    ).length
);
const embeddingCount = computed(
  () =>
    tableData.value.filter(item =>
      hasPurpose(item.purpose, AiServicePurpose.Embedding)
    ).length
);

const normalizePurpose = (value: number) => {
  if (value === AiServicePurpose.Llm || value === AiServicePurpose.Embedding)
    return value;
  if (value === AiServicePurpose.None) return AiServicePurpose.Llm;
  return value;
};

const getDefaultPriority = (purpose: AiServicePurpose) => {
  const samePurpose = tableData.value
    .filter(item => item.purpose === purpose)
    .map(item => item.priority ?? 0);
  if (samePurpose.length === 0) return 0;
  return Math.max(...samePurpose) + 1;
};

watch(
  () => formData.purpose,
  value => {
    if (value === AiServicePurpose.Llm) {
      formData.embeddingModel = "";
    } else if (value === AiServicePurpose.Embedding) {
      formData.llmModel = "";
    }
  }
);

const handleAdd = (purpose: AiServicePurpose) => {
  if (
    !ensurePermission("btn:ai-service:create", "权限不足，无法新增AI服务配置")
  ) {
    return;
  }
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
    embeddingModel:
      purpose === AiServicePurpose.Embedding ? "nomic-embed-text" : "",
    llmModel: "",
    disableThinking: false,
    defaultRecallTopK: DEFAULT_RECALL_TOP_K
  });
  dialogVisible.value = true;
};

const handleEdit = async (row: AiServiceConfig) => {
  if (
    !ensurePermission("btn:ai-service:update", "权限不足，无法编辑AI服务配置")
  ) {
    return;
  }
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
        ElMessage.warning(
          "检测到用途同时包含 LLM 与 Embedding，请重新选择单一用途"
        );
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
        llmModel: detail.llmModel ?? "",
        disableThinking: !!detail.disableThinking,
        defaultRecallTopK: detail.defaultRecallTopK ?? DEFAULT_RECALL_TOP_K
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
  if (
    !ensurePermission("btn:ai-service:delete", "权限不足，无法删除AI服务配置")
  ) {
    return;
  }
  try {
    await ElMessageBox.confirm(`确定删除配置“${row.name}”吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteAiService(row.id);
    if (res.code === 0) {
      if (activeTestResult.value?.rowId === row.id) {
        clearInlineTestResult();
      }
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
  if (
    !ensurePermission("btn:ai-service:test", "权限不足，无法测试AI服务配置")
  ) {
    return;
  }
  const testingKey = row.id;
  if (isRowLoading(testingState, testingKey)) {
    return;
  }

  setRowLoading(testingState, testingKey, true);
  try {
    const res = await testAiServiceConnection(row.id);
    if (res.code === 0) {
      await showInlineTestResult(buildInlineTestResultCard(row, res.data));
    } else {
      await showInlineTestResult(
        buildInlineTestResultCard(row, {
          success: false,
          message: res.message || "连接测试失败",
          elapsedMs: 0,
          serviceElapsedMs: null,
          httpStatusCode: null,
          targetModel:
            row.purpose === AiServicePurpose.Llm
              ? row.llmModel
              : row.embeddingModel,
          targetEndpoint: row.endpoint,
          hostPort: null
        })
      );
    }
  } catch (error) {
    await showInlineTestResult(
      buildInlineTestResultCard(row, {
        success: false,
        message: extractErrorMessage(error, "连接测试失败"),
        elapsedMs: 0,
        serviceElapsedMs: null,
        httpStatusCode: null,
        targetModel:
          row.purpose === AiServicePurpose.Llm
            ? row.llmModel
            : row.embeddingModel,
        targetEndpoint: row.endpoint,
        hostPort: null
      })
    );
  } finally {
    setRowLoading(testingState, testingKey, false);
  }
};

const handleProbeModels = async (row: AiServiceConfig) => {
  if (
    !ensurePermission("btn:ai-service:models", "权限不足，无法探测AI服务模型")
  ) {
    return;
  }
  if (isRowLoading(probingState, row.id)) {
    return;
  }

  modelsInfo.id = row.id;
  modelsInfo.name = row.name;
  modelsInfo.purpose = row.purpose;
  modelsInfo.llmModels = [];
  modelsInfo.embeddingModels = [];
  modelsInfo.message = "正在探测远端模型，请稍候...";
  modelsDialogVisible.value = true;
  await loadModels();
};

const loadModels = async () => {
  if (
    !ensurePermission("btn:ai-service:models", "权限不足，无法探测AI服务模型")
  ) {
    return;
  }
  if (!modelsInfo.id) return;

  setRowLoading(probingState, modelsInfo.id, true);
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
  } catch (error) {
    modelsInfo.message = extractErrorMessage(error, "模型探测失败");
  } finally {
    modelsLoading.value = false;
    setRowLoading(probingState, modelsInfo.id, false);
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
  if (
    !ensurePermission(
      isEdit.value ? "btn:ai-service:update" : "btn:ai-service:create",
      isEdit.value
        ? "权限不足，无法保存AI服务配置"
        : "权限不足，无法新增AI服务配置"
    )
  ) {
    return;
  }
  if (!formData.name.trim()) {
    ElMessage.warning("请输入名称");
    return;
  }
  if (!formData.purpose) {
    ElMessage.warning("请至少选择一个用途");
    return;
  }
  if (
    formData.purpose !== AiServicePurpose.Llm &&
    formData.purpose !== AiServicePurpose.Embedding
  ) {
    ElMessage.warning("用途只能选择一个（LLM 或 Embedding）");
    return;
  }
  if (formData.purpose === AiServicePurpose.Llm && !formData.llmModel.trim()) {
    ElMessage.warning("请输入 LLM 模型");
    return;
  }
  if (
    formData.purpose === AiServicePurpose.Embedding &&
    !formData.embeddingModel.trim()
  ) {
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
    llmModel,
    disableThinking: !!formData.disableThinking,
    defaultRecallTopK: Math.min(
      MAX_RECALL_TOP_K,
      Math.max(1, formData.defaultRecallTopK || DEFAULT_RECALL_TOP_K)
    )
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
        <el-button type="primary" link @click="showAllConfigs = true"
          >查看全部</el-button
        >
      </template>
    </el-alert>

    <div class="service-grid">
      <el-card v-loading="loading" class="service-card">
        <template #header>
          <div class="card-header">
            <span>LLM 服务</span>
            <div class="card-actions">
              <el-button
                v-if="canCreate"
                type="primary"
                @click="handleAdd(AiServicePurpose.Llm)"
              >
                新增
              </el-button>
              <template v-if="llmConfig && hasSummaryActionButtons">
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="handleEdit(llmConfig)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="canDelete"
                  type="danger"
                  link
                  @click="handleDelete(llmConfig)"
                >
                  删除
                </el-button>
                <el-button
                  v-if="canProbeModels"
                  type="success"
                  link
                  :loading="isRowLoading(probingState, llmConfig.id)"
                  :disabled="isRowLoading(probingState, llmConfig.id)"
                  @click="handleProbeModels(llmConfig)"
                >
                  模型
                </el-button>
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
            <div class="config-value">
              {{ getServiceTypeLabel(llmConfig.serviceType) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">优先级</div>
            <div class="config-value">{{ llmConfig.priority }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">Endpoint</div>
            <div class="config-value">
              {{ formatValue(llmConfig.endpoint) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">LLM 模型</div>
            <div class="config-value">
              {{ formatValue(llmConfig.llmModel) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">关闭思考模式</div>
            <div class="config-value">
              {{ llmConfig.disableThinking ? "已开启" : "未开启" }}
            </div>
          </div>
        </div>
      </el-card>

      <el-card v-loading="loading" class="service-card">
        <template #header>
          <div class="card-header">
            <span>Embedding 服务</span>
            <div class="card-actions">
              <el-button
                v-if="canCreate"
                type="primary"
                @click="handleAdd(AiServicePurpose.Embedding)"
              >
                新增
              </el-button>
              <template v-if="embeddingConfig && hasSummaryActionButtons">
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="handleEdit(embeddingConfig)"
                >
                  编辑
                </el-button>
                <el-button
                  v-if="canDelete"
                  type="danger"
                  link
                  @click="handleDelete(embeddingConfig)"
                >
                  删除
                </el-button>
                <el-button
                  v-if="canProbeModels"
                  type="success"
                  link
                  :loading="isRowLoading(probingState, embeddingConfig.id)"
                  :disabled="isRowLoading(probingState, embeddingConfig.id)"
                  @click="handleProbeModels(embeddingConfig)"
                >
                  模型
                </el-button>
              </template>
            </div>
          </div>
        </template>
        <el-empty v-if="!embeddingConfig" description="未配置 Embedding 服务" />
        <div v-else class="config-grid">
          <div class="config-row">
            <div class="config-label">名称</div>
            <div class="config-value">
              {{ formatValue(embeddingConfig.name) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">类型</div>
            <div class="config-value">
              {{ getServiceTypeLabel(embeddingConfig.serviceType) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">优先级</div>
            <div class="config-value">{{ embeddingConfig.priority }}</div>
          </div>
          <div class="config-row">
            <div class="config-label">Endpoint</div>
            <div class="config-value">
              {{ formatValue(embeddingConfig.endpoint) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">Embedding 模型</div>
            <div class="config-value">
              {{ formatValue(embeddingConfig.embeddingModel) }}
            </div>
          </div>
          <div class="config-row">
            <div class="config-label">匹配链路</div>
            <div class="config-value">证据裁决</div>
          </div>
          <div class="config-row">
            <div class="config-label">默认召回候选数</div>
            <div class="config-value">
              {{ embeddingConfig.defaultRecallTopK }}
            </div>
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
      <el-table
        :data="tableData"
        stripe
        :row-key="row => String(row.id)"
        :expand-row-keys="expandedTestRowKeys"
      >
        <el-table-column
          type="expand"
          width="1"
          class-name="test-result-expand-column"
        >
          <template #default="{ row }">
            <div
              v-if="activeTestResult && activeTestResult.rowId === row.id"
              :id="`ai-test-result-${row.id}`"
              class="ai-test-result-shell"
            >
              <div
                class="ai-test-result-card"
                :class="
                  getTestResultCardClass(
                    activeTestResult?.category || 'general',
                    !!activeTestResult?.success
                  )
                "
              >
                <div class="ai-test-result-card__header">
                  <div>
                    <div class="ai-test-result-card__title">
                      {{ activeTestResult?.rowName }} ·
                      {{ activeTestResult?.summary }}
                    </div>
                    <div class="ai-test-result-card__subtitle">
                      {{ activeTestResult?.statusText }}
                    </div>
                  </div>
                  <el-button link type="info" @click="clearInlineTestResult">
                    收起
                  </el-button>
                </div>

                <div class="ai-test-result-card__tags">
                  <el-tag
                    v-for="tag in activeTestResult?.tags || []"
                    :key="`${row.id}-${tag.label}`"
                    size="small"
                    :type="tag.type"
                    effect="light"
                  >
                    {{ tag.label }}
                  </el-tag>
                </div>

                <div class="ai-test-result-card__message">
                  {{ activeTestResult?.message }}
                </div>

                <div class="ai-test-result-card__details">
                  <div
                    v-for="detail in activeTestResult?.details || []"
                    :key="`${row.id}-${detail.label}`"
                    class="ai-test-result-card__detail"
                  >
                    <span class="ai-test-result-card__detail-label">{{
                      detail.label
                    }}</span>
                    <span class="ai-test-result-card__detail-value">{{
                      detail.value
                    }}</span>
                  </div>
                </div>
              </div>
            </div>
          </template>
        </el-table-column>
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
        <el-table-column
          prop="embeddingModel"
          label="EmbeddingModel"
          min-width="160"
        />
        <el-table-column prop="llmModel" label="LLMModel" min-width="160" />
        <el-table-column label="关闭思考模式" width="140">
          <template #default="{ row }">
            {{ row.disableThinking ? "是" : "否" }}
          </template>
        </el-table-column>
        <el-table-column
          v-if="hasActionButtons"
          label="操作"
          width="300"
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
              v-if="canDelete"
              type="danger"
              link
              @click="handleDelete(row)"
            >
              删除
            </el-button>
            <el-button
              v-if="canTest"
              type="warning"
              link
              :loading="isRowLoading(testingState, row.id)"
              :disabled="isRowLoading(testingState, row.id)"
              @click="handleTest(row)"
            >
              完整测试
            </el-button>
            <el-button
              v-if="canProbeModels"
              type="success"
              link
              :loading="isRowLoading(probingState, row.id)"
              :disabled="isRowLoading(probingState, row.id)"
              @click="handleProbeModels(row)"
            >
              模型
            </el-button>
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
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Embedding)"
          label="匹配链路"
        >
          <div class="thinking-config">
            <el-tag type="success">证据裁决</el-tag>
            <div class="thinking-tip">
              固定执行 Embedding 召回、证据重排、冲突门禁和高歧义复核。
            </div>
          </div>
        </el-form-item>
        <el-form-item
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Embedding)"
          label="默认召回数"
        >
          <el-input-number
            v-model="formData.defaultRecallTopK"
            :min="1"
            :max="MAX_RECALL_TOP_K"
            controls-position="right"
          />
        </el-form-item>
        <el-form-item
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Llm)"
          label="LLMModel"
          required
        >
          <el-input v-model="formData.llmModel" />
        </el-form-item>
        <el-form-item
          v-if="hasPurpose(formData.purpose, AiServicePurpose.Llm)"
          label="关闭思考模式"
        >
          <div class="thinking-config">
            <el-switch v-model="formData.disableThinking" />
            <div class="thinking-tip">
              当前主要对 Ollama 生效，系统会优先请求关闭思考输出，并对
              `&lt;think&gt;` 内容做兜底清理
            </div>
          </div>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="handleSubmit">
          确定
        </el-button>
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
        <div
          v-if="
            !modelsLoading &&
            hasPurpose(modelsInfo.purpose, AiServicePurpose.Llm)
          "
          class="model-section"
        >
          <div class="model-label">LLM 模型</div>
          <div v-if="modelsInfo.llmModels.length" class="model-tags">
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
        <div
          v-if="
            !modelsLoading &&
            hasPurpose(modelsInfo.purpose, AiServicePurpose.Embedding)
          "
          class="model-section"
        >
          <div class="model-label">Embedding 模型</div>
          <div v-if="modelsInfo.embeddingModels.length" class="model-tags">
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
        <el-button
          v-if="canProbeModels"
          type="primary"
          :loading="modelsLoading"
          @click="loadModels"
        >
          重新探测
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

.thinking-config {
  display: flex;
  flex-direction: column;
  gap: 6px;
  width: 100%;
}

.thinking-tip {
  font-size: 12px;
  color: #6b7280;
  line-height: 1.5;
}

.service-table {
  margin-top: 8px;
}

:deep(.test-result-expand-column .cell),
:deep(.test-result-expand-column .el-table__expand-icon) {
  width: 0;
  padding: 0;
  margin: 0;
  overflow: hidden;
}

:deep(.test-result-expand-column) {
  border-right: none;
}

.ai-test-result-shell {
  padding: 8px 0 4px;
}

.ai-test-result-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px 18px;
  border-radius: 14px;
  border: 1px solid #dbeafe;
  background: linear-gradient(180deg, #f8fbff 0%, #f3f8ff 100%);
  box-shadow: 0 10px 24px rgb(15 23 42 / 6%);
}

.ai-test-result-card__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.ai-test-result-card__title {
  font-size: 14px;
  font-weight: 600;
  color: #0f172a;
  line-height: 1.5;
}

.ai-test-result-card__subtitle {
  margin-top: 4px;
  font-size: 12px;
  color: #64748b;
}

.ai-test-result-card__tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.ai-test-result-card__message {
  font-size: 13px;
  line-height: 1.7;
  color: #1e293b;
  word-break: break-word;
}

.ai-test-result-card__details {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
}

.ai-test-result-card__detail {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 10px 12px;
  border-radius: 10px;
  background: rgb(255 255 255 / 68%);
}

.ai-test-result-card__detail-label {
  font-size: 12px;
  color: #64748b;
}

.ai-test-result-card__detail-value {
  font-size: 13px;
  color: #0f172a;
  line-height: 1.5;
  word-break: break-word;
}

.ai-test-result-card--success {
  border-color: #bbf7d0;
  background: linear-gradient(180deg, #f3fff7 0%, #ecfdf3 100%);
}

.ai-test-result-card--auth {
  border-color: #fecaca;
  background: linear-gradient(180deg, #fff7f7 0%, #fef2f2 100%);
}

.ai-test-result-card--endpoint {
  border-color: #fed7aa;
  background: linear-gradient(180deg, #fffaf5 0%, #fff7ed 100%);
}

.ai-test-result-card--rate-limit,
.ai-test-result-card--timeout {
  border-color: #fde68a;
  background: linear-gradient(180deg, #fffdf2 0%, #fefce8 100%);
}

.ai-test-result-card--remote,
.ai-test-result-card--general {
  border-color: #cbd5e1;
  background: linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%);
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
