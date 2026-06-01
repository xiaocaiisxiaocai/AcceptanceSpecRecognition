<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  AiServicePurpose,
  createAiService,
  deleteAiService,
  getAiServiceById,
  getAiServiceList,
  setAiServiceDisabled,
  testAiServiceConnection,
  updateAiService,
  type AiServiceConfig
} from "@/api/ai-service";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";
import { getRequestErrorMessage } from "@/utils/error-message";
import {
  buildAiServiceConfigSummary,
  getDefaultPriority,
} from "./config-selection";
import AiServiceConfigsTable from "./components/AiServiceConfigsTable.vue";
import AiServiceEditDialog from "./components/AiServiceEditDialog.vue";
import AiServiceModelsDialog from "./components/AiServiceModelsDialog.vue";
import AiServiceSummaryCard from "./components/AiServiceSummaryCard.vue";
import {
  buildCreateAiServicePayload,
  buildUpdateAiServicePayload,
  clearModelOutsidePurpose,
  createEditAiServiceFormData,
  createEmptyAiServiceFormData,
  createNewAiServiceFormData,
  getAiServiceDialogTitle,
  getAiServiceSubmitPermission,
  getAiServiceSubmitSuccessMessage,
  validateAiServiceFormData
} from "./form";
import { useAiServiceModelsProbe } from "./composables/useAiServiceModelsProbe";
import {
  buildInlineTestResultCard,
  type InlineTestResultCard
} from "./test-result";
import {
  hasPurpose,
  isRowLoading,
  setRowLoading
} from "./utils";

defineOptions({
  name: "AiServicesConfig"
});

const loading = ref(false);
const tableData = ref<AiServiceConfig[]>([]);
const showAllConfigs = ref(false);
const testingState = reactive<Record<string, boolean>>({});
const disabledState = reactive<Record<string, boolean>>({});
const expandedTestRowKeys = ref<string[]>([]);

const activeTestResult = ref<InlineTestResultCard | null>(null);

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
const formData = reactive(createEmptyAiServiceFormData());
const {
  probingState,
  modelsDialogVisible,
  modelsLoading,
  modelsInfo,
  handleProbeModels,
  loadModels,
  copyModelName
} = useAiServiceModelsProbe();

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

const extractErrorMessage = (error: unknown, fallback: string) =>
  getRequestErrorMessage(error, fallback);

const configSummary = computed(() =>
  buildAiServiceConfigSummary(tableData.value)
);
const llmConfig = computed(() => configSummary.value.llmConfig);
const embeddingConfig = computed(() => configSummary.value.embeddingConfig);
const llmCount = computed(() => configSummary.value.llmCount);
const embeddingCount = computed(() => configSummary.value.embeddingCount);

watch(
  () => formData.purpose,
  () => clearModelOutsidePurpose(formData)
);

const handleAdd = (purpose: AiServicePurpose) => {
  if (
    !ensurePermission("btn:ai-service:create", "权限不足，无法新增AI服务配置")
  ) {
    return;
  }
  isEdit.value = false;
  dialogTitle.value = getAiServiceDialogTitle(isEdit.value);
  originalApiKey.value = "";
  Object.assign(
    formData,
    createNewAiServiceFormData(
      purpose,
      getDefaultPriority(tableData.value, purpose)
    )
  );
  dialogVisible.value = true;
};

const handleEdit = async (row: AiServiceConfig) => {
  if (
    !ensurePermission("btn:ai-service:update", "权限不足，无法编辑AI服务配置")
  ) {
    return;
  }
  isEdit.value = true;
  dialogTitle.value = getAiServiceDialogTitle(isEdit.value);
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
      Object.assign(formData, createEditAiServiceFormData(detail));
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

const handleToggleDisabled = async (row: AiServiceConfig) => {
  if (
    !ensurePermission("btn:ai-service:update", "权限不足，无法修改AI服务配置")
  ) {
    return;
  }

  const nextDisabled = !row.isDisabled;
  try {
    await ElMessageBox.confirm(
      nextDisabled
        ? `确定禁用配置“${row.name}”吗？禁用后不会被智能填充等 AI 功能使用。`
        : `确定启用配置“${row.name}”吗？`,
      "提示",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: nextDisabled ? "warning" : "info"
      }
    );

    setRowLoading(disabledState, row.id, true);
    const res = await setAiServiceDisabled(row.id, nextDisabled);
    if (res.code === 0) {
      if (nextDisabled && activeTestResult.value?.rowId === row.id) {
        clearInlineTestResult();
      }
      ElMessage.success(nextDisabled ? "已禁用" : "已启用");
      await loadData();
    } else {
      ElMessage.error(res.message || "操作失败");
    }
  } catch (error) {
    if (error !== "cancel" && error !== "close") {
      ElMessage.error(extractErrorMessage(error, "操作失败"));
    }
  } finally {
    setRowLoading(disabledState, row.id, false);
  }
};

const handleTest = async (row: AiServiceConfig) => {
  if (
    !ensurePermission("btn:ai-service:test", "权限不足，无法测试AI服务配置")
  ) {
    return;
  }
  if (row.isDisabled) {
    ElMessage.warning("该配置已禁用，不能测试");
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

const handleSubmit = async () => {
  const submitPermission = getAiServiceSubmitPermission(isEdit.value);
  if (
    !ensurePermission(submitPermission.code, submitPermission.message)
  ) {
    return;
  }
  const validateMessage = validateAiServiceFormData(formData);
  if (validateMessage) {
    ElMessage.warning(validateMessage);
    return;
  }

  try {
    const res = await (async () => {
      if (isEdit.value) {
        return updateAiService(
          formData.id,
          buildUpdateAiServicePayload(formData, originalApiKey.value)
        );
      }

      return createAiService(buildCreateAiServicePayload(formData));
    })();

    if (res.code === 0) {
      ElMessage.success(getAiServiceSubmitSuccessMessage(isEdit.value));
      dialogVisible.value = false;
      loadData();
    } else {
      ElMessage.error(res.message);
    }
  } catch (error) {
    ElMessage.error(extractErrorMessage(error, "操作失败"));
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
      <el-button v-if="tableData.length" @click="showAllConfigs = !showAllConfigs">
        {{ showAllConfigs ? "收起全部" : "查看全部" }}
      </el-button>
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
      <AiServiceSummaryCard
        title="LLM 服务"
        :purpose="AiServicePurpose.Llm"
        empty-description="未配置 LLM 服务"
        :config="llmConfig"
        :loading="loading"
        :can-create="canCreate"
        :can-update="canUpdate"
        :can-delete="canDelete"
        :can-probe-models="canProbeModels"
        :has-summary-action-buttons="hasSummaryActionButtons"
        :disabled-state="disabledState"
        :probing-state="probingState"
        @add="handleAdd"
        @edit="handleEdit"
        @toggle-disabled="handleToggleDisabled"
        @delete="handleDelete"
        @probe-models="handleProbeModels"
      />

      <AiServiceSummaryCard
        title="Embedding 服务"
        :purpose="AiServicePurpose.Embedding"
        empty-description="未配置 Embedding 服务"
        :config="embeddingConfig"
        :loading="loading"
        :can-create="canCreate"
        :can-update="canUpdate"
        :can-delete="canDelete"
        :can-probe-models="canProbeModels"
        :has-summary-action-buttons="hasSummaryActionButtons"
        :disabled-state="disabledState"
        :probing-state="probingState"
        @add="handleAdd"
        @edit="handleEdit"
        @toggle-disabled="handleToggleDisabled"
        @delete="handleDelete"
        @probe-models="handleProbeModels"
      />
    </div>

    <AiServiceConfigsTable
      v-if="showAllConfigs"
      :table-data="tableData"
      :expanded-test-row-keys="expandedTestRowKeys"
      :active-test-result="activeTestResult"
      :has-action-buttons="hasActionButtons"
      :can-update="canUpdate"
      :can-delete="canDelete"
      :can-test="canTest"
      :can-probe-models="canProbeModels"
      :disabled-state="disabledState"
      :testing-state="testingState"
      :probing-state="probingState"
      @collapse="showAllConfigs = false"
      @clear-test-result="clearInlineTestResult"
      @edit="handleEdit"
      @toggle-disabled="handleToggleDisabled"
      @delete="handleDelete"
      @test="handleTest"
      @probe-models="handleProbeModels"
    />

    <AiServiceEditDialog
      v-model="dialogVisible"
      :form-data="formData"
      :title="dialogTitle"
      :can-submit="canSubmit"
      @submit="handleSubmit"
    />

    <AiServiceModelsDialog
      v-model="modelsDialogVisible"
      :loading="modelsLoading"
      :can-probe-models="canProbeModels"
      :models-info="modelsInfo"
      @reload="loadModels"
      @copy-model-name="copyModelName"
    />
  </div>
</template>

<style scoped src="./index.styles.css"></style>
