<script setup lang="ts">
import {
  computed,
  onActivated,
  onBeforeUnmount,
  onDeactivated,
  onMounted,
  ref,
  watch
} from "vue";
import { useRouter } from "vue-router";
import type { AiServiceSelection } from "@/api/ai-service";
import { getRequestErrorMessage } from "@/utils/error-message";
import { hasPerms } from "@/utils/auth";
import { createAiSelectionRetryController } from "@/utils/ai-selection-retry";
import {
  getRuntimeAiPurposeResult,
  loadRuntimeAiSelectionsSettled
} from "@/utils/runtime-ai-selection-loader";
import { getDistinctAiServiceModel } from "./ai-service-display";
import { resolveAiAssistSelectionState } from "./ai-selection-state";

const props = defineProps<{
  enabled: boolean;
  serviceId?: number;
}>();

const emit = defineEmits<{
  (event: "update:enabled", value: boolean): void;
  (event: "update:serviceId", value: number | undefined): void;
}>();

const router = useRouter();
const llmSelection = ref<AiServiceSelection>({ status: "checking" });
const embeddingSelection = ref<AiServiceSelection>({ status: "checking" });
const loading = ref(true);
let requestController: AbortController | undefined;
let lastLoadStartedAt = 0;
const hasServices = computed(
  () =>
    llmSelection.value.status === "available" &&
    llmSelection.value.serviceId != null &&
    embeddingSelection.value.status === "available" &&
    embeddingSelection.value.serviceId != null
);
const llmServiceModel = computed(() =>
  getDistinctAiServiceModel(llmSelection.value.name, llmSelection.value.model)
);
const embeddingServiceModel = computed(() =>
  getDistinctAiServiceModel(
    embeddingSelection.value.name,
    embeddingSelection.value.model
  )
);
const canConfigureAiServices = computed(() =>
  hasPerms("page:config:ai-services")
);

const getUnavailableStatusText = (
  selection: AiServiceSelection,
  label: "LLM" | "Embedding"
) => {
  if (selection.status === "available" && selection.serviceId != null) {
    return "";
  }
  if (selection.message) return selection.message;
  return selection.status === "checking"
    ? `正在检测 ${label} 服务可用性`
    : `当前没有可用的 ${label} 服务`;
};

const unavailableTitle = computed(() => {
  const statuses = [
    getUnavailableStatusText(llmSelection.value, "LLM"),
    getUnavailableStatusText(embeddingSelection.value, "Embedding")
  ].filter(Boolean);
  const action = canConfigureAiServices.value
    ? "请完成 AI 服务配置或重新检测"
    : "已使用规则识别；请联系管理员配置";
  return `${statuses.join("；")}；${action}`;
});

const goToAiServices = () => router.push("/config/ai-services");

const loadServices = async (resetRetry = true) => {
  const startedAt = Date.now();
  if (startedAt - lastLoadStartedAt < 250) return;
  lastLoadStartedAt = startedAt;
  if (resetRetry) aiSelectionRetry.cancel();
  requestController?.abort();
  const controller = new AbortController();
  requestController = controller;
  loading.value = true;
  try {
    const results = await loadRuntimeAiSelectionsSettled(
      ["embedding", "llm"],
      controller.signal
    );
    if (requestController !== controller) return;

    const llmResult = getRuntimeAiPurposeResult(results, "llm");
    const embeddingResult = getRuntimeAiPurposeResult(results, "embedding");
    if (!llmResult || !embeddingResult) return;
    if (
      llmResult.kind === "cancelled" ||
      embeddingResult.kind === "cancelled"
    ) {
      return;
    }

    llmSelection.value = llmResult.selection;
    embeddingSelection.value = embeddingResult.selection;
    aiSelectionRetry.schedule([llmSelection.value, embeddingSelection.value]);
    const next = resolveAiAssistSelectionState(
      llmSelection.value,
      embeddingSelection.value
    );
    if (props.enabled !== next.enabled) emit("update:enabled", next.enabled);
    if (props.serviceId !== next.serviceId) {
      emit("update:serviceId", next.serviceId);
    }
  } catch (error) {
    if (
      error instanceof Error &&
      (error.name === "CanceledError" || error.name === "AbortError")
    ) {
      return;
    }
    const message = getRequestErrorMessage(error, "加载 AI 服务失败");
    llmSelection.value = { status: "checking", message };
    embeddingSelection.value = { status: "checking", message };
    aiSelectionRetry.schedule([llmSelection.value, embeddingSelection.value]);
    if (props.enabled) emit("update:enabled", false);
    if (props.serviceId != null) emit("update:serviceId", undefined);
  } finally {
    if (requestController === controller) loading.value = false;
  }
};

const aiSelectionRetry = createAiSelectionRetryController({
  refresh: () => void loadServices(false),
  maxAttempts: 10,
  retryStatuses: ["checking", "unavailable"],
  delayMsByStatus: { unavailable: 5000 }
});

watch([() => props.enabled, () => props.serviceId], ([enabled, serviceId]) => {
  if (!enabled && props.serviceId != null) {
    emit("update:serviceId", undefined);
    return;
  }
  if (
    enabled &&
    serviceId == null &&
    hasServices.value &&
    llmSelection.value.serviceId != null
  ) {
    emit("update:serviceId", llmSelection.value.serviceId);
  }
});

onMounted(loadServices);
onActivated(loadServices);
const stopPendingSelection = () => {
  requestController?.abort();
  aiSelectionRetry.cancel();
};

onDeactivated(stopPendingSelection);
onBeforeUnmount(stopPendingSelection);
</script>

<template>
  <div class="structure-ai-control">
    <div class="structure-ai-header">
      <div>
        <div class="structure-ai-title">AI 辅助疑难识别</div>
        <div class="structure-ai-description">
          仅在模板和规则难以判断时调用 AI；关闭后仍可识别，确认后仍会学习。
        </div>
      </div>
      <el-switch
        :model-value="enabled"
        :disabled="loading || !hasServices"
        @update:model-value="value => emit('update:enabled', Boolean(value))"
      />
    </div>

    <div
      v-if="enabled && hasServices"
      class="structure-ai-service"
      role="status"
    >
      <span class="structure-ai-service-label">自动使用</span>
      <div class="structure-ai-service-list">
        <div class="structure-ai-service-row">
          <span class="structure-ai-purpose">LLM</span>
          <span class="structure-ai-service-name">{{ llmSelection.name }}</span>
          <span v-if="llmServiceModel" class="structure-ai-service-model">
            {{ llmServiceModel }}
          </span>
        </div>
        <div class="structure-ai-service-row">
          <span class="structure-ai-purpose">Embedding</span>
          <span class="structure-ai-service-name">
            {{ embeddingSelection.name }}
          </span>
          <span v-if="embeddingServiceModel" class="structure-ai-service-model">
            {{ embeddingServiceModel }}
          </span>
        </div>
      </div>
    </div>

    <div v-if="!hasServices" class="structure-ai-unavailable">
      <el-alert
        :type="
          llmSelection.status === 'checking' ||
          embeddingSelection.status === 'checking' ||
          loading
            ? 'info'
            : 'warning'
        "
        :closable="false"
        :title="unavailableTitle"
        class="structure-ai-alert"
      />
      <el-button
        v-if="canConfigureAiServices"
        type="primary"
        link
        @click="goToAiServices"
      >
        去配置 AI 服务
      </el-button>
      <el-button
        v-if="canConfigureAiServices"
        link
        :loading="loading"
        @click="() => loadServices()"
      >
        重新检测
      </el-button>
    </div>
  </div>
</template>

<style scoped>
.structure-ai-control {
  padding: 12px 14px;
  margin-top: 12px;
  background: var(--el-fill-color-blank);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}

.structure-ai-header {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
}

.structure-ai-title {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.structure-ai-description {
  margin-top: 3px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.structure-ai-service {
  display: flex;
  gap: 8px;
  align-items: flex-start;
  margin-top: 12px;
  font-size: 13px;
}

.structure-ai-service-label {
  padding-top: 2px;
  color: var(--el-text-color-secondary);
}

.structure-ai-service-list {
  display: grid;
  gap: 6px;
}

.structure-ai-service-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.structure-ai-purpose {
  min-width: 72px;
  color: var(--el-text-color-secondary);
}

.structure-ai-service-name {
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.structure-ai-service-model {
  color: var(--el-text-color-secondary);
}

.structure-ai-unavailable {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-top: 10px;
}

.structure-ai-alert {
  flex: 1;
}
</style>
