<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from "vue";
import {
  DEFAULT_HIGH_CONFIDENCE_THRESHOLD,
  MAX_RECALL_TOP_K,
  type MatchingMode,
  type MatchConfig,
  defaultMatchConfig
} from "@/api/matching";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import {
  getAiServiceList,
  AiServicePurpose,
  sortAiServicesByPriority,
  type AiServiceConfig
} from "@/api/ai-service";
import { ElMessage } from "element-plus";
import { getRequestErrorMessage } from "@/utils/error-message";
import { loadAllPagedItems } from "@/utils/paged-options";
import type { SmartFillScope } from "../smartFillExecution.helpers";

const props = defineProps<{
  modelValue?: MatchConfig;
  allowLlm?: boolean;
  scope?: SmartFillScope;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: MatchConfig): void;
  (
    e: "scopeChange",
    customerId?: number,
    processId?: number,
    machineModelId?: number
  ): void;
}>();

// 匹配配置
const config = ref<MatchConfig>({ ...defaultMatchConfig });

// 范围选择
const customers = ref<Customer[]>([]);
const processes = ref<Process[]>([]);
const machineModels = ref<MachineModel[]>([]);
const selectedCustomerId = ref<number | undefined>(undefined);
const selectedProcessId = ref<number | undefined>(undefined);
const selectedMachineModelId = ref<number | undefined>(undefined);
let syncingScopeFromParent = false;
const loadingCustomers = ref(false);
const loadingProcesses = ref(false);
const loadingMachineModels = ref(false);
const loadingAiServices = ref(false);
let customerOptionsController: AbortController | undefined;
let processOptionsController: AbortController | undefined;
let machineModelOptionsController: AbortController | undefined;
const embeddingServices = ref<AiServiceConfig[]>([]);
const llmServices = ref<AiServiceConfig[]>([]);
const allowLlm = computed(() => props.allowLlm !== false);
const hasAvailableEmbeddingService = computed(
  () => embeddingServices.value.length > 0
);
const hasAvailableLlmService = computed(() => llmServices.value.length > 0);
const matchingModeOptions: Array<{
  label: string;
  value: MatchingMode;
  hint: string;
}> = [
  {
    label: "项目+规格",
    value: "projectSpecification",
    hint: "保持现有匹配方式"
  },
  {
    label: "仅规格",
    value: "specificationOnly",
    hint: "允许项目不一致时按规格命中"
  }
];
const matchConfigSyncKeys = [
  "embeddingServiceId",
  "llmServiceId",
  "minScoreThreshold",
  "highConfidenceThreshold",
  "recallTopK",
  "ambiguityMargin",
  "llmParallelism",
  "llmRowTimeoutSeconds",
  "llmRetryCount",
  "llmCircuitBreakFailures",
  "matchingMode",
  "enableLlmEquivalenceAdjudication",
  "llmEquivalenceMinConfidence",
  "enableDeterministicAutoApply",
  "llmMaxCallsPerBatch",
  "exactMatchOnly",
  "filterEmptySourceRows",
  "enableLlmSemanticPriority",
  "llmSemanticRecallThreshold",
  "embeddingSemanticAutoApplyThreshold"
] satisfies Array<keyof MatchConfig>;
const syncMatchConfigField = <K extends keyof MatchConfig>(
  key: K,
  source: MatchConfig
) => {
  if (config.value[key] !== source[key]) {
    config.value[key] = source[key];
  }
};

const hasExplicitMatchingDefaults = computed(() => {
  const incoming = props.modelValue;
  if (!incoming) {
    return false;
  }

  return (
    incoming.recallTopK !== undefined &&
    incoming.recallTopK !== defaultMatchConfig.recallTopK
  );
});

// 高级选项展开
const showAdvanced = ref(false);

// 标记：正在从内部更新到外部，避免回写时触发整体替换
let isInternalUpdate = false;

// 同步 modelValue → config（仅在外部驱动时逐属性更新，避免整体替换导致 el-select 失去选中状态）
watch(
  () => props.modelValue,
  val => {
    if (isInternalUpdate) return;
    const source = { ...defaultMatchConfig, ...val };
    for (const key of matchConfigSyncKeys) {
      syncMatchConfigField(key, source);
    }
  },
  { immediate: true }
);

// 触发配置更新
const updateConfig = () => {
  isInternalUpdate = true;
  emit("update:modelValue", { ...config.value });
  // 下一个微任务恢复标记
  Promise.resolve().then(() => {
    isInternalUpdate = false;
  });
};

watch(config, updateConfig, { deep: true });

watch(
  () => [config.value.recallTopK, config.value.ambiguityMargin],
  () => {
    if (!config.value.recallTopK || config.value.recallTopK < 1) {
      config.value.recallTopK = defaultMatchConfig.recallTopK;
    } else if (config.value.recallTopK > MAX_RECALL_TOP_K) {
      config.value.recallTopK = MAX_RECALL_TOP_K;
    }
    if (
      config.value.ambiguityMargin === undefined ||
      config.value.ambiguityMargin === null
    ) {
      config.value.ambiguityMargin = defaultMatchConfig.ambiguityMargin;
    }
  },
  { immediate: true }
);

// 加载客户列表
const loadCustomers = async () => {
  customerOptionsController?.abort();
  const controller = new AbortController();
  customerOptionsController = controller;
  loadingCustomers.value = true;
  try {
    const items = await loadAllPagedItems(
      (page, pageSize, signal) =>
        getCustomerList({ page, pageSize }, { signal }),
      { getKey: item => item.id, signal: controller.signal }
    );
    if (customerOptionsController === controller) customers.value = items;
  } catch (error) {
    if (!controller.signal.aborted) {
      ElMessage.error(getRequestErrorMessage(error, "加载客户列表失败"));
    }
  } finally {
    if (customerOptionsController === controller) {
      customerOptionsController = undefined;
      loadingCustomers.value = false;
    }
  }
};

// 加载制程列表
const loadProcesses = async () => {
  processOptionsController?.abort();
  const controller = new AbortController();
  processOptionsController = controller;
  loadingProcesses.value = true;
  try {
    const items = await loadAllPagedItems(
      (page, pageSize, signal) =>
        getProcessList({ page, pageSize }, { signal }),
      { getKey: item => item.id, signal: controller.signal }
    );
    if (processOptionsController === controller) processes.value = items;
  } catch (error) {
    if (!controller.signal.aborted) {
      ElMessage.error(getRequestErrorMessage(error, "加载制程列表失败"));
    }
  } finally {
    if (processOptionsController === controller) {
      processOptionsController = undefined;
      loadingProcesses.value = false;
    }
  }
};

// 加载机型列表
const loadMachineModels = async () => {
  machineModelOptionsController?.abort();
  const controller = new AbortController();
  machineModelOptionsController = controller;
  loadingMachineModels.value = true;
  try {
    const items = await loadAllPagedItems(
      (page, pageSize, signal) =>
        getMachineModelList({ page, pageSize }, { signal }),
      { getKey: item => item.id, signal: controller.signal }
    );
    if (machineModelOptionsController === controller) {
      machineModels.value = items;
    }
  } catch (error) {
    if (!controller.signal.aborted) {
      ElMessage.error(getRequestErrorMessage(error, "加载机型列表失败"));
    }
  } finally {
    if (machineModelOptionsController === controller) {
      machineModelOptionsController = undefined;
      loadingMachineModels.value = false;
    }
  }
};

// 加载 AI 服务列表
const loadAiServices = async () => {
  loadingAiServices.value = true;
  try {
    const res = await getAiServiceList({ page: 1, pageSize: 200 });
    if (res.code === 0) {
      const items = res.data.items;
      const enabledItems = items.filter(item => !item.isDisabled);
      embeddingServices.value = sortAiServicesByPriority(
        enabledItems.filter(
          s =>
            (s.purpose & AiServicePurpose.Embedding) ===
              AiServicePurpose.Embedding && !!s.embeddingModel
        )
      );
      llmServices.value = sortAiServicesByPriority(
        enabledItems.filter(
          s =>
            (s.purpose & AiServicePurpose.Llm) === AiServicePurpose.Llm &&
            !!s.llmModel
        )
      );
      if (
        config.value.embeddingServiceId &&
        !embeddingServices.value.some(
          service => service.id === config.value.embeddingServiceId
        )
      ) {
        config.value.embeddingServiceId = undefined;
      }
      if (
        config.value.llmServiceId &&
        !llmServices.value.some(
          service => service.id === config.value.llmServiceId
        )
      ) {
        config.value.llmServiceId = undefined;
      }
      // 自动选择第一个可用服务（如果尚未选择）
      if (
        !config.value.embeddingServiceId &&
        embeddingServices.value.length > 0
      ) {
        config.value.embeddingServiceId = embeddingServices.value[0].id;
      }
      if (!config.value.llmServiceId && llmServices.value.length > 0) {
        config.value.llmServiceId = llmServices.value[0].id;
      }
      if (!hasExplicitMatchingDefaults.value) {
        applyEmbeddingServiceDefaults(config.value.embeddingServiceId);
      }
    } else {
      ElMessage.error(res.message || "加载AI服务失败");
    }
  } catch {
    ElMessage.error("加载AI服务失败");
  } finally {
    loadingAiServices.value = false;
  }
};

const applyEmbeddingServiceDefaults = (serviceId?: number) => {
  if (!serviceId) return;
  const selectedService = embeddingServices.value.find(
    item => item.id === serviceId
  );
  if (!selectedService) return;

  config.value.recallTopK = Math.min(
    MAX_RECALL_TOP_K,
    Math.max(1, selectedService.defaultRecallTopK)
  );
};

watch(
  () => config.value.embeddingServiceId,
  (serviceId, previousServiceId) => {
    if (!serviceId || serviceId === previousServiceId) {
      return;
    }

    if (previousServiceId === undefined && hasExplicitMatchingDefaults.value) {
      return;
    }

    applyEmbeddingServiceDefaults(serviceId);
  }
);

watch(
  () =>
    [
      props.scope?.customerId,
      props.scope?.processId,
      props.scope?.machineModelId
    ] as const,
  ([customerId, processId, machineModelId]) => {
    syncingScopeFromParent = true;
    selectedCustomerId.value = customerId;
    selectedProcessId.value = processId;
    selectedMachineModelId.value = machineModelId;
    syncingScopeFromParent = false;
  },
  { immediate: true, flush: "sync" }
);

watch(
  [selectedCustomerId, selectedProcessId, selectedMachineModelId],
  () => {
    if (syncingScopeFromParent) return;
    emit(
      "scopeChange",
      selectedCustomerId.value,
      selectedProcessId.value,
      selectedMachineModelId.value
    );
  },
  { flush: "sync" }
);

// 重置配置
const resetConfig = () => {
  const embeddingServiceId = config.value.embeddingServiceId;
  const llmServiceId = config.value.llmServiceId;
  config.value = {
    ...defaultMatchConfig,
    embeddingServiceId,
    llmServiceId
  };
  applyEmbeddingServiceDefaults(config.value.embeddingServiceId);
};

onMounted(() => {
  loadCustomers();
  loadProcesses();
  loadMachineModels();
  loadAiServices();
});

onBeforeUnmount(() => {
  customerOptionsController?.abort();
  processOptionsController?.abort();
  machineModelOptionsController?.abort();
});

// 暴露方法
defineExpose({
  resetConfig,
  getScope: () => ({
    customerId: selectedCustomerId.value,
    processId: selectedProcessId.value,
    machineModelId: selectedMachineModelId.value
  }),
  getServiceStatus: () => ({
    hasAvailableEmbeddingService: hasAvailableEmbeddingService.value,
    hasAvailableLlmService: hasAvailableLlmService.value
  })
});
</script>

<template>
  <div class="match-config">
    <!-- 匹配范围 -->
    <div class="config-section">
      <div class="section-title">匹配范围</div>
      <el-form :inline="true" class="scope-form filter-form">
        <el-form-item label="客户">
          <el-select
            v-model="selectedCustomerId"
            placeholder="全部客户"
            :loading="loadingCustomers"
            filterable
            clearable
            :teleported="true"
            class="search-select search-select--200"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="customer in customers"
              :key="customer.id"
              :label="customer.name"
              :value="customer.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="制程">
          <el-select
            v-model="selectedProcessId"
            placeholder="全部制程"
            :loading="loadingProcesses"
            filterable
            clearable
            :teleported="true"
            class="search-select search-select--200"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="process in processes"
              :key="process.id"
              :label="process.name"
              :value="process.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="机型">
          <el-select
            v-model="selectedMachineModelId"
            placeholder="全部机型"
            :loading="loadingMachineModels"
            filterable
            clearable
            :teleported="true"
            class="search-select search-select--200"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="model in machineModels"
              :key="model.id"
              :label="model.name"
              :value="model.id"
            />
          </el-select>
        </el-form-item>
      </el-form>
      <div class="scope-tip">
        <el-icon><InfoFilled /></el-icon>
        <span>不选择则匹配所有验收规格</span>
      </div>
    </div>

    <!-- 基础配置 -->
    <div class="config-section">
      <div class="section-title">匹配设置</div>
      <el-form label-width="130px">
        <el-form-item label="仅精确匹配">
          <div class="exact-match-option">
            <el-switch
              v-model="config.exactMatchOnly"
              active-text="开启"
              inactive-text="关闭"
            />
            <div class="exact-match-option__title">仅匹配项目+规格完全一致</div>
            <div class="form-inline-tip">
              开启后无需
              AI/Embedding，只采用项目+规格完全一致的规格；未命中行可手工填写验收标准和备注。
            </div>
            <el-alert
              v-if="!loadingAiServices && !hasAvailableEmbeddingService"
              type="info"
              :closable="false"
              show-icon
              title="Embedding 不可用时可开启此模式"
              description="即使未配置可用 Embedding，也可以开启仅精确匹配继续预览。"
              class="service-status-alert"
            />
          </div>
        </el-form-item>
        <el-form-item label="Embedding 服务">
          <el-select
            v-model="config.embeddingServiceId"
            placeholder="请选择 Embedding 服务"
            :teleported="true"
            :loading="loadingAiServices"
            style="width: 100%; max-width: 400px"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="service in embeddingServices"
              :key="service.id"
              :label="`${service.name}（${service.embeddingModel || '-'}）`"
              :value="service.id"
            />
          </el-select>
          <el-alert
            v-if="!loadingAiServices && !hasAvailableEmbeddingService"
            type="warning"
            :closable="false"
            show-icon
            title="未检测到可用 Embedding 服务"
            :description="
              config.exactMatchOnly
                ? '当前已开启仅精确匹配，可继续预览完全一致命中；如需语义召回，请启用 Embedding 服务。'
                : '请先在 AI 服务配置中启用至少一个带 Embedding 模型的服务，或开启上方仅精确匹配。'
            "
            class="service-status-alert"
          />
        </el-form-item>
        <el-form-item label="LLM 服务">
          <el-select
            v-model="config.llmServiceId"
            placeholder="请选择 LLM 服务"
            :teleported="true"
            :disabled="!allowLlm"
            :loading="loadingAiServices"
            style="width: 100%; max-width: 400px"
            popper-class="app-select-popper"
          >
            <el-option
              v-for="service in llmServices"
              :key="service.id"
              :label="`${service.name}（${service.llmModel || '-'}）`"
              :value="service.id"
            />
          </el-select>
          <el-alert
            v-if="allowLlm && !loadingAiServices && !hasAvailableLlmService"
            type="info"
            :closable="false"
            show-icon
            title="未检测到可用 LLM 服务"
            description="当前仍可执行 Embedding 召回和证据重排；如需 AI 复核，请先启用至少一个带 LLM 模型的服务。"
            class="service-status-alert"
          />
        </el-form-item>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="匹配链路">
              <div class="fixed-mode">
                <el-tag type="success">证据裁决</el-tag>
              </div>
              <div class="form-inline-tip">
                固定执行 Embedding 召回、证据重排、冲突门禁和高歧义复核。
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="匹配方式">
              <el-radio-group
                v-model="config.matchingMode"
                class="match-mode-group"
              >
                <el-radio-button
                  v-for="option in matchingModeOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </el-radio-button>
              </el-radio-group>
              <div class="form-inline-tip">
                {{
                  matchingModeOptions.find(
                    item => item.value === config.matchingMode
                  )?.hint ?? "保持现有匹配方式"
                }}
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="最小得分阈值">
              <el-slider
                v-model="config.minScoreThreshold"
                :min="0"
                :max="1"
                :step="0.05"
                :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                show-input
                :show-input-controls="false"
              />
              <div class="form-inline-tip">
                该阈值仅用于保留候选，不决定自动采用
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="高置信阈值">
              <el-slider
                v-model="config.highConfidenceThreshold"
                :min="0.5"
                :max="1"
                :step="0.01"
                :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                show-input
                :show-input-controls="false"
              />
              <div class="form-inline-tip">
                高置信阈值会参与确定性自动通过与结果分层；默认值为
                {{ (DEFAULT_HIGH_CONFIDENCE_THRESHOLD * 100).toFixed(0) }}%
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="过滤空行">
              <el-switch
                v-model="config.filterEmptySourceRows"
                active-text="开启"
                inactive-text="关闭"
              />
              <div class="form-inline-tip">
                关闭后会保留项目列和规格列都为空的行
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="确定性自动通过">
              <el-switch
                v-model="config.enableDeterministicAutoApply"
                active-text="开启"
                inactive-text="关闭"
                :disabled="config.exactMatchOnly"
              />
              <div class="form-inline-tip">
                无硬冲突且达到高置信阈值时直接采用，不进入同步 AI
                裁决。仅精确匹配模式下无效。
              </div>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="LLM 语义优先">
              <el-switch
                v-model="config.enableLlmSemanticPriority"
                active-text="开启"
                inactive-text="关闭"
                :disabled="
                  config.exactMatchOnly || !allowLlm || !hasAvailableLlmService
                "
              />
              <div class="form-inline-tip">
                开启后 LLM
                裁决具有最高权威：扩大召回范围、覆盖未知单位/品牌、忽略硬冲突拦截。速度变慢但命中率更高。
              </div>
            </el-form-item>
          </el-col>
          <el-col v-if="config.enableLlmSemanticPriority" :span="12">
            <el-form-item label="语义召回下限">
              <el-slider
                v-model="config.llmSemanticRecallThreshold"
                :min="0.1"
                :max="0.9"
                :step="0.05"
                show-input
                :show-input-controls="false"
                style="width: 200px"
              />
              <div class="form-inline-tip">
                Embedding 分 ≥ 此值的候选进入 LLM 视野（默认
                0.5），越低召回越多但 LLM 调用越多。
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="高Emb自动通过">
              <el-input-number
                v-model="config.embeddingSemanticAutoApplyThreshold"
                :min="0"
                :max="1"
                :step="0.01"
                :precision="2"
                controls-position="right"
              />
              <div class="form-inline-tip">
                候选 Embedding≥此值且无硬冲突时直接自动通过（即使 AI
                不确定）；0=关闭。越高越严。
              </div>
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="召回候选数">
              <el-input-number
                v-model="config.recallTopK"
                :min="1"
                :max="MAX_RECALL_TOP_K"
                :step="1"
                controls-position="right"
              />
              <div class="form-inline-tip">
                第一阶段最多保留多少个候选进入重排
              </div>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="歧义分差阈值">
              <el-input-number
                v-model="config.ambiguityMargin"
                :min="0"
                :max="1"
                :step="0.01"
                :precision="2"
                controls-position="right"
              />
              <div class="form-inline-tip">
                Top1 与 Top2 分差不超过该值时标记为高歧义
              </div>
            </el-form-item>
          </el-col>
        </el-row>
        <el-alert
          type="info"
          :closable="false"
          show-icon
          :title="`默认先完成快速预览，不在同步阶段逐行调用 AI 等价裁决；需要精度优先时可在高级选项中开启。高置信阈值 ${((config.highConfidenceThreshold ?? DEFAULT_HIGH_CONFIDENCE_THRESHOLD) * 100).toFixed(0)}% 会参与确定性自动通过与结果分层。`"
        />
      </el-form>
    </div>

    <!-- 高级选项 -->
    <div class="config-section">
      <div
        class="section-header"
        role="button"
        tabindex="0"
        :aria-expanded="showAdvanced"
        @click="showAdvanced = !showAdvanced"
        @keydown.enter="showAdvanced = !showAdvanced"
        @keydown.space.prevent="showAdvanced = !showAdvanced"
      >
        <span class="section-title">LLM 复核</span>
        <el-icon :class="{ rotated: showAdvanced }">
          <ArrowRight />
        </el-icon>
      </div>

      <el-collapse-transition>
        <div v-show="showAdvanced" class="advanced-options">
          <el-form label-width="140px">
            <el-alert
              v-if="!allowLlm"
              type="warning"
              :closable="false"
              show-icon
              title="当前账号没有 LLM 复核权限，本页仅保留基础匹配能力。"
              class="mb-4"
            />
            <el-row :gutter="20" align="middle" class="llm-row">
              <el-col :span="8">
                <el-form-item label="AI 等价裁决">
                  <el-switch
                    v-model="config.enableLlmEquivalenceAdjudication"
                    active-text="开启"
                    inactive-text="关闭"
                    :disabled="
                      config.exactMatchOnly ||
                      !allowLlm ||
                      !hasAvailableLlmService
                    "
                  />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  默认关闭以优先保证预览速度；开启后，达到最小得分阈值的当前最佳候选会在同步匹配阶段进入
                  AI 等价裁决。
                </span>
              </el-col>
            </el-row>
            <el-row
              v-if="
                config.enableLlmEquivalenceAdjudication ||
                config.enableLlmSemanticPriority
              "
              :gutter="20"
              align="middle"
              class="llm-row"
            >
              <el-col :span="8">
                <el-form-item label="等价置信下限">
                  <el-input-number
                    v-model="config.llmEquivalenceMinConfidence"
                    :min="0"
                    :max="1"
                    :step="0.05"
                    :precision="2"
                    :disabled="!allowLlm || !hasAvailableLlmService"
                    size="default"
                    controls-position="right"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  LLM 判定等价但自评置信度低于此值时转人工确认；设为 0
                  表示不设门槛。语义优先模式下，该值是覆盖硬冲突的关键护栏。
                </span>
              </el-col>
            </el-row>
            <!-- LLM并行度 -->
            <el-row :gutter="20" align="middle">
              <el-col :span="8">
                <el-form-item label="LLM并行数">
                  <el-input-number
                    v-model="config.llmParallelism"
                    :min="1"
                    :max="10"
                    :step="1"
                    :disabled="!allowLlm"
                    size="default"
                    controls-position="right"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  同时处理的行数，值越大速度越快但占用资源越多；本地 Ollama 建议
                  1-4
                </span>
              </el-col>
            </el-row>
            <el-row :gutter="20" align="middle">
              <el-col :span="8">
                <el-form-item label="LLM调用上限">
                  <el-input-number
                    v-model="config.llmMaxCallsPerBatch"
                    :min="0"
                    :max="200"
                    :step="1"
                    :disabled="!allowLlm"
                    size="default"
                    controls-position="right"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  同一批次内重排和等价裁决共享该预算；设为 0 时不调用同步 LLM。
                </span>
              </el-col>
            </el-row>
            <div class="llm-hint">
              LLM
              仅负责复核“是否可直接采用现有规格”，不会生成新验收标准参与落库。
            </div>

            <div class="reset-btn">
              <el-button size="small" @click="resetConfig"
                >重置为默认值</el-button
              >
            </div>
          </el-form>
        </div>
      </el-collapse-transition>
    </div>
  </div>
</template>

<script lang="ts">
import { InfoFilled, ArrowRight } from "@element-plus/icons-vue";
export default {
  components: { InfoFilled, ArrowRight }
};
</script>

<style scoped>
.match-config {
  width: 100%;
}

.config-section {
  padding-bottom: 16px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.config-section:last-child {
  margin-bottom: 0;
  border-bottom: none;
}

.section-title {
  margin-bottom: 12px;
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  cursor: pointer;
  user-select: none;
}

.section-header .el-icon {
  transition: transform 0.3s;
}

.section-header .el-icon.rotated {
  transform: rotate(90deg);
}

.scope-form {
  margin-bottom: 8px;
}

.scope-tip {
  display: flex;
  gap: 4px;
  align-items: center;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.advanced-options {
  padding-top: 16px;
}

.service-status-alert {
  margin-top: 12px;
}

.exact-match-option {
  width: 100%;
}

.exact-match-option__title {
  display: inline-flex;
  align-items: center;
  margin-left: 12px;
  font-size: 13px;
  font-weight: 600;
  color: var(--color-text);
}

.llm-row {
  margin-top: 8px;
}

.llm-hint {
  margin-top: 4px;
  font-size: 12px;
  color: var(--app-text-secondary);
}

.parallelism-hint {
  font-size: 12px;
  line-height: 32px;
  color: var(--app-text-disabled);
}

.form-inline-tip {
  margin-left: 8px;
  font-size: 12px;
  color: var(--app-text-disabled);
}

.reset-btn {
  margin-top: 12px;
  text-align: right;
}

/* P1-1: 优化表单密度 */
.match-config .el-form-item {
  margin-bottom: 12px !important;
}

.match-config .el-row {
  row-gap: 8px !important;
}

.match-config .el-col {
  display: flex;
  flex-direction: column;
}

/* 减少alert与相邻元素的间距 */
.match-config .service-status-alert {
  margin-top: 4px !important;
  margin-bottom: 0 !important;
}

.match-config .exact-match-option__title {
  margin-bottom: 2px !important;
}

/* 减少form-inline-tip的上方间距 */
.match-config .form-inline-tip {
  margin-top: 0 !important;
  margin-bottom: 0 !important;
}
</style>
