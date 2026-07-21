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
import {
  getAiServiceSelection,
  type AiServiceSelection
} from "@/api/ai-service";
import { getRequestErrorMessage } from "@/utils/error-message";
import { hasPerms } from "@/utils/auth";
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
const selection = ref<AiServiceSelection>({ status: "checking" });
const loading = ref(true);
const loadError = ref("");
let requestController: AbortController | undefined;
let lastLoadStartedAt = 0;
let retryTimer: ReturnType<typeof setTimeout> | undefined;
let checkingAttempts = 0;
const hasServices = computed(
  () =>
    selection.value.status === "available" && selection.value.serviceId != null
);
const selectedService = computed(() =>
  selection.value.status === "available" ? selection.value : undefined
);
const selectedServiceModel = computed(() =>
  getDistinctAiServiceModel(
    selectedService.value?.name,
    selectedService.value?.model
  )
);
const canConfigureAiServices = computed(() =>
  hasPerms("page:config:ai-services")
);
const unavailableTitle = computed(() => {
  if (selection.value.status === "checking") {
    return loadError.value
      ? `${loadError.value}，正在重试；完成前仍可使用规则识别`
      : "正在检测 LLM 服务可用性；完成前仍可使用规则识别";
  }
  if (selection.value.message) return selection.value.message;
  return canConfigureAiServices.value
    ? "当前没有可用的 LLM 服务，请先完成 AI 服务配置"
    : "当前没有可用的 LLM 服务，已使用规则识别；请联系管理员配置";
});

const goToAiServices = () => router.push("/config/ai-services");

const loadServices = async () => {
  const startedAt = Date.now();
  if (startedAt - lastLoadStartedAt < 250) return;
  lastLoadStartedAt = startedAt;
  if (retryTimer !== undefined) {
    globalThis.clearTimeout(retryTimer);
    retryTimer = undefined;
  }
  requestController?.abort();
  const controller = new AbortController();
  requestController = controller;
  loading.value = true;
  loadError.value = "";
  try {
    const response = await getAiServiceSelection("llm", controller.signal);
    if (requestController !== controller) return;
    if (response.code !== 0) {
      selection.value = {
        status: "unavailable",
        message: response.message || "LLM 服务当前不可用"
      };
      checkingAttempts = 0;
      emit("update:serviceId", undefined);
      if (props.enabled) emit("update:enabled", false);
      return;
    }

    selection.value = response.data;
    if (response.data.status === "checking" && checkingAttempts < 10) {
      checkingAttempts += 1;
      retryTimer = globalThis.setTimeout(() => void loadServices(), 1500);
    } else {
      checkingAttempts = 0;
    }
    const next = resolveAiAssistSelectionState(response.data);
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
    selection.value = { status: "checking" };
    loadError.value = getRequestErrorMessage(error, "加载 LLM 服务失败");
    if (checkingAttempts < 10) {
      checkingAttempts += 1;
      retryTimer = globalThis.setTimeout(() => void loadServices(), 1500);
    }
  } finally {
    if (requestController === controller) loading.value = false;
  }
};

watch([() => props.enabled, () => props.serviceId], ([enabled, serviceId]) => {
  if (!enabled && props.serviceId != null) {
    emit("update:serviceId", undefined);
    return;
  }
  if (
    enabled &&
    serviceId == null &&
    selectedService.value?.serviceId != null
  ) {
    emit("update:serviceId", selectedService.value.serviceId);
  }
});

onMounted(loadServices);
onActivated(loadServices);
const stopPendingSelection = () => {
  requestController?.abort();
  if (retryTimer !== undefined) globalThis.clearTimeout(retryTimer);
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
      v-if="enabled && selectedService"
      class="structure-ai-service"
      role="status"
    >
      <span class="structure-ai-service-label">自动使用</span>
      <span class="structure-ai-service-name">{{ selectedService.name }}</span>
      <span v-if="selectedServiceModel" class="structure-ai-service-model">
        {{ selectedServiceModel }}
      </span>
    </div>

    <div v-if="!hasServices" class="structure-ai-unavailable">
      <el-alert
        :type="selection.status === 'checking' || loading ? 'info' : 'warning'"
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
        v-if="canConfigureAiServices && selection.status === 'checking'"
        link
        :loading="loading"
        @click="loadServices"
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
  align-items: center;
  margin-top: 12px;
  font-size: 13px;
}

.structure-ai-service-label {
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
