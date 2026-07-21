<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { useRouter } from "vue-router";
import {
  AiServicePurpose,
  getAiServiceList,
  sortAiServicesByPriority,
  type AiServiceConfig
} from "@/api/ai-service";
import { getRequestErrorMessage } from "@/utils/error-message";
import { hasPerms } from "@/utils/auth";
import { getDistinctAiServiceModel } from "./ai-service-display";

const props = defineProps<{
  enabled: boolean;
  serviceId?: number;
}>();

const emit = defineEmits<{
  (event: "update:enabled", value: boolean): void;
  (event: "update:serviceId", value: number | undefined): void;
}>();

const router = useRouter();
const services = ref<AiServiceConfig[]>([]);
const loading = ref(true);
const loadError = ref("");
const hasServices = computed(() => services.value.length > 0);
const selectedService = computed(
  () =>
    services.value.find(service => service.id === props.serviceId) ??
    services.value[0]
);
const selectedServiceModel = computed(() =>
  getDistinctAiServiceModel(
    selectedService.value?.name,
    selectedService.value?.llmModel
  )
);
const canConfigureAiServices = computed(() =>
  hasPerms("page:config:ai-services")
);
const unavailableTitle = computed(() => {
  if (loadError.value) {
    return `${loadError.value}，已使用规则识别`;
  }
  return canConfigureAiServices.value
    ? "当前没有可用的 LLM 服务，请先完成 AI 服务配置"
    : "当前没有可用的 LLM 服务，已使用规则识别；请联系管理员配置";
});

const goToAiServices = () => router.push("/config/ai-services");

const loadServices = async () => {
  loading.value = true;
  loadError.value = "";
  try {
    const response = await getAiServiceList({ page: 1, pageSize: 200 });
    if (response.code !== 0) {
      throw new Error(response.message || "加载 LLM 服务失败");
    }

    services.value = sortAiServicesByPriority(
      response.data.items.filter(
        service =>
          !service.isDisabled &&
          (service.purpose & AiServicePurpose.Llm) === AiServicePurpose.Llm &&
          !!service.llmModel
      )
    );

    const defaultService = services.value[0];
    if (defaultService) {
      if (!props.enabled) {
        emit("update:enabled", true);
      }
      if (
        props.serviceId == null ||
        !services.value.some(service => service.id === props.serviceId)
      ) {
        emit("update:serviceId", defaultService.id);
      }
    } else {
      if (props.serviceId != null) {
        emit("update:serviceId", undefined);
      }
      if (props.enabled) {
        emit("update:enabled", false);
      }
    }
  } catch (error) {
    services.value = [];
    loadError.value = getRequestErrorMessage(error, "加载 LLM 服务失败");
    emit("update:serviceId", undefined);
    if (props.enabled) emit("update:enabled", false);
  } finally {
    loading.value = false;
  }
};

watch([() => props.enabled, () => props.serviceId], ([enabled, serviceId]) => {
  if (!enabled && props.serviceId != null) {
    emit("update:serviceId", undefined);
    return;
  }
  if (enabled && serviceId == null && services.value[0]) {
    const defaultService = services.value[0];
    emit("update:serviceId", defaultService.id);
  }
});

onMounted(loadServices);
</script>

<template>
  <div class="structure-ai-control">
    <div class="structure-ai-header">
      <div>
        <div class="structure-ai-title">AI 增强结构识别</div>
        <div class="structure-ai-description">
          默认使用可用 AI 服务增强识别；关闭后仅使用本地规则。
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

    <div v-if="!loading && !hasServices" class="structure-ai-unavailable">
      <el-alert
        type="info"
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
