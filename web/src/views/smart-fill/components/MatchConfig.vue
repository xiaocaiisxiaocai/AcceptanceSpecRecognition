<script setup lang="ts">
import {
  computed,
  onActivated,
  onBeforeUnmount,
  onDeactivated,
  onMounted,
  nextTick,
  ref,
  watch
} from "vue";
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
import type { AiServiceSelection } from "@/api/ai-service";
import { ElMessage } from "element-plus";
import { getRequestErrorMessage } from "@/utils/error-message";
import { loadAllPagedItems } from "@/utils/paged-options";
import type { SmartFillScope } from "../smartFillExecution.helpers";
import { getDistinctAiServiceModel } from "@/views/shared/ai-service-display";
import { createAiSelectionRetryController } from "@/utils/ai-selection-retry";
import {
  getRuntimeAiPurposeResult,
  loadRuntimeAiSelectionsSettled,
  type RuntimeAiSelectionRefreshResult
} from "@/utils/runtime-ai-selection-loader";
import {
  getRuntimeAiSelectionStatusText,
  isRuntimeAiSelectionAvailable
} from "@/utils/runtime-ai-selection";
import { applyMatchConfigRuntimeAiSelections } from "./match-config-ai-selection";

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
let aiSelectionController: AbortController | undefined;
let aiSelectionVersion = 0;
let aiSelectionRequest: Promise<RuntimeAiSelectionRefreshResult> | undefined;
const embeddingSelection = ref<AiServiceSelection>({ status: "checking" });
const llmSelection = ref<AiServiceSelection>({ status: "checking" });
const allowLlm = computed(() => props.allowLlm !== false);
const hasAvailableEmbeddingService = computed(() =>
  isRuntimeAiSelectionAvailable(embeddingSelection.value)
);
const hasAvailableLlmService = computed(() =>
  isRuntimeAiSelectionAvailable(llmSelection.value)
);
const embeddingServiceModel = computed(() =>
  getDistinctAiServiceModel(
    embeddingSelection.value.name,
    embeddingSelection.value.model
  )
);
const llmServiceModel = computed(() =>
  getDistinctAiServiceModel(llmSelection.value.name, llmSelection.value.model)
);
const embeddingStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(embeddingSelection.value, "Embedding")
);
const llmStatusText = computed(() =>
  getRuntimeAiSelectionStatusText(llmSelection.value, "LLM")
);
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

// 高级选项展开
const showMatchingAdvanced = ref(false);
const showAdvanced = ref(false);
let revealExpandedSectionTimer: number | undefined;

const cancelExpandedSectionReveal = () => {
  if (revealExpandedSectionTimer == null) return;
  window.clearTimeout(revealExpandedSectionTimer);
  revealExpandedSectionTimer = undefined;
};

const revealExpandedSection = async (elementId: string) => {
  await nextTick();
  cancelExpandedSectionReveal();
  revealExpandedSectionTimer = window.setTimeout(() => {
    document.getElementById(elementId)?.scrollIntoView({
      block: "start",
      behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches
        ? "auto"
        : "smooth"
    });
    revealExpandedSectionTimer = undefined;
  }, 320);
};

const toggleMatchingAdvanced = () => {
  cancelExpandedSectionReveal();
  showMatchingAdvanced.value = !showMatchingAdvanced.value;
  if (showMatchingAdvanced.value) {
    void revealExpandedSection("matching-advanced-options");
  }
};

const toggleLlmAdvanced = () => {
  cancelExpandedSectionReveal();
  showAdvanced.value = !showAdvanced.value;
  if (showAdvanced.value) {
    void revealExpandedSection("llm-review-options");
  }
};

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

// 加载运行时可用的 AI 服务
const loadAiServicesOnce = async () => {
  aiSelectionController?.abort();
  const controller = new AbortController();
  const version = ++aiSelectionVersion;
  aiSelectionController = controller;
  loadingAiServices.value = true;

  try {
    const results = await loadRuntimeAiSelectionsSettled(
      ["embedding", "llm"],
      controller.signal
    );
    if (
      aiSelectionController !== controller ||
      version !== aiSelectionVersion ||
      controller.signal.aborted
    ) {
      return { current: false, version };
    }

    const embeddingResult = getRuntimeAiPurposeResult(results, "embedding");
    const llmResult = getRuntimeAiPurposeResult(results, "llm");
    if (!embeddingResult || !llmResult) {
      return { current: false, version };
    }

    embeddingSelection.value = embeddingResult.selection;
    llmSelection.value = llmResult.selection;
    applyMatchConfigRuntimeAiSelections(
      config.value,
      embeddingResult.selection,
      llmResult.selection
    );
    aiSelectionRetry.schedule([embeddingResult.selection, llmResult.selection]);

    return {
      current: true,
      version,
      embedding: embeddingResult.selection,
      llm: llmResult.selection
    };
  } finally {
    if (aiSelectionController === controller) {
      aiSelectionController = undefined;
      loadingAiServices.value = false;
    }
  }
};

const aiSelectionRetry = createAiSelectionRetryController({
  refresh: () => void loadAiServices(false)
});
const loadAiServices = (resetRetry = true) => {
  if (resetRetry) aiSelectionRetry.cancel();
  if (aiSelectionRequest) return aiSelectionRequest;

  const request = loadAiServicesOnce();
  aiSelectionRequest = request;
  void request.finally(() => {
    if (aiSelectionRequest === request) aiSelectionRequest = undefined;
  });
  return request;
};

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
};

onMounted(() => {
  loadCustomers();
  loadProcesses();
  loadMachineModels();
  loadAiServices();
});

onActivated(() => {
  void loadAiServices();
});

onBeforeUnmount(() => {
  customerOptionsController?.abort();
  processOptionsController?.abort();
  machineModelOptionsController?.abort();
  stopAiSelectionRequests();
  cancelExpandedSectionReveal();
});

const stopAiSelectionRequests = () => {
  aiSelectionVersion += 1;
  aiSelectionController?.abort();
  aiSelectionRequest = undefined;
  aiSelectionRetry.cancel();
};

onDeactivated(stopAiSelectionRequests);

// 暴露方法
defineExpose({
  resetConfig,
  refreshAiServices: loadAiServices,
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
          <div
            v-if="hasAvailableEmbeddingService"
            class="automatic-service"
            role="status"
            aria-live="polite"
          >
            <span>自动使用</span>
            <strong>{{ embeddingSelection.name }}</strong>
            <small v-if="embeddingServiceModel">
              {{ embeddingServiceModel }}
            </small>
          </div>
          <el-alert
            v-else
            :type="
              loadingAiServices || embeddingSelection.status === 'checking'
                ? 'info'
                : 'warning'
            "
            :closable="false"
            show-icon
            :title="embeddingStatusText"
            :description="
              config.exactMatchOnly
                ? '当前已开启仅精确匹配，可继续预览完全一致命中；如需语义召回，请启用 Embedding 服务。'
                : '运行状态确认可用前不能执行语义匹配；请稍后重试，或开启上方仅精确匹配。'
            "
            class="service-status-alert"
          />
        </el-form-item>
        <el-form-item label="LLM 服务">
          <div
            v-if="allowLlm && hasAvailableLlmService"
            class="automatic-service"
            role="status"
            aria-live="polite"
          >
            <span>自动使用</span>
            <strong>{{ llmSelection.name }}</strong>
            <small v-if="llmServiceModel">{{ llmServiceModel }}</small>
          </div>
          <el-alert
            v-else-if="allowLlm"
            type="info"
            :closable="false"
            show-icon
            :title="llmStatusText"
            description="当前仍可执行 Embedding 召回和证据重排；仅在 LLM 运行状态确认可用后启用 AI 复核。"
            class="service-status-alert"
          />
          <el-alert
            v-else
            type="info"
            :closable="false"
            show-icon
            title="当前账号没有 LLM 复核权限"
            class="service-status-alert"
          />
        </el-form-item>
        <el-row :gutter="20">
          <el-col :xs="24" :md="12">
            <el-form-item label="匹配链路">
              <div class="fixed-mode">
                <el-tag type="success">证据裁决</el-tag>
              </div>
              <div class="form-inline-tip">
                固定执行 Embedding 召回、证据重排、冲突门禁和高歧义复核。
              </div>
            </el-form-item>
          </el-col>
          <el-col :xs="24" :md="12">
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
          <el-col :xs="24" :md="12">
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
        </el-row>

        <div class="matching-strategy-summary">
          <div>
            <span>当前策略</span>
            <strong>
              {{ config.exactMatchOnly ? "仅精确匹配" : "Embedding 证据匹配" }}
            </strong>
          </div>
          <div class="matching-strategy-summary__metrics">
            <span>最多 {{ config.recallTopK }} 个候选</span>
            <span>
              最低得分
              {{ ((config.minScoreThreshold ?? 0) * 100).toFixed(0) }}%
            </span>
            <span>
              高置信
              {{
                (
                  (config.highConfidenceThreshold ??
                    DEFAULT_HIGH_CONFIDENCE_THRESHOLD) * 100
                ).toFixed(0)
              }}%
            </span>
          </div>
        </div>

        <button
          type="button"
          class="section-header section-header--button"
          :aria-expanded="showMatchingAdvanced"
          aria-controls="matching-advanced-options"
          @click="toggleMatchingAdvanced"
        >
          <span>
            <span class="section-title">高级匹配参数</span>
            <small>阈值、自动通过、语义优先与候选控制</small>
          </span>
          <el-icon :class="{ rotated: showMatchingAdvanced }">
            <ArrowRight />
          </el-icon>
        </button>

        <el-collapse-transition>
          <div
            v-show="showMatchingAdvanced"
            id="matching-advanced-options"
            class="advanced-options matching-advanced-options"
          >
            <el-row :gutter="20">
              <el-col :xs="24" :md="12">
                <el-form-item label="最低候选得分">
                  <el-slider
                    v-model="config.minScoreThreshold"
                    :min="0"
                    :max="1"
                    :step="0.05"
                    :format-tooltip="
                      (val: number) => `${(val * 100).toFixed(0)}%`
                    "
                    show-input
                    :show-input-controls="false"
                  />
                  <div class="form-inline-tip">
                    仅用于过滤候选，不直接决定是否自动采用。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="高置信阈值">
                  <el-slider
                    v-model="config.highConfidenceThreshold"
                    :min="0.5"
                    :max="1"
                    :step="0.01"
                    :format-tooltip="
                      (val: number) => `${(val * 100).toFixed(0)}%`
                    "
                    show-input
                    :show-input-controls="false"
                  />
                  <div class="form-inline-tip">
                    高置信阈值会参与确定性自动通过与结果分层；默认
                    {{
                      (DEFAULT_HIGH_CONFIDENCE_THRESHOLD * 100).toFixed(0)
                    }}%。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="确定性自动通过">
                  <el-switch
                    v-model="config.enableDeterministicAutoApply"
                    active-text="开启"
                    inactive-text="关闭"
                    :disabled="config.exactMatchOnly"
                  />
                  <div class="form-inline-tip">
                    无硬冲突且达到高置信阈值时直接采用。仅精确匹配模式下无效。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="LLM 语义优先">
                  <el-switch
                    v-model="config.enableLlmSemanticPriority"
                    active-text="开启"
                    inactive-text="关闭"
                    :disabled="
                      config.exactMatchOnly ||
                      !allowLlm ||
                      !hasAvailableLlmService
                    "
                  />
                  <div class="form-inline-tip">
                    扩大语义召回并提高 LLM 裁决权重，命中范围更广但处理更慢。
                  </div>
                </el-form-item>
              </el-col>
              <el-col v-if="config.enableLlmSemanticPriority" :xs="24" :md="12">
                <el-form-item label="语义召回下限">
                  <el-slider
                    v-model="config.llmSemanticRecallThreshold"
                    :min="0.1"
                    :max="0.9"
                    :step="0.05"
                    show-input
                    :show-input-controls="false"
                  />
                  <div class="form-inline-tip">
                    达到该分数的候选进入 LLM 视野，越低调用越多。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="高相似自动通过">
                  <el-input-number
                    v-model="config.embeddingSemanticAutoApplyThreshold"
                    :min="0"
                    :max="1"
                    :step="0.01"
                    :precision="2"
                    controls-position="right"
                  />
                  <div class="form-inline-tip">
                    无硬冲突且 Embedding 达到该值时自动通过；0 表示关闭。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
                <el-form-item label="每条最多候选数">
                  <el-input-number
                    v-model="config.recallTopK"
                    :min="1"
                    :max="MAX_RECALL_TOP_K"
                    :step="1"
                    controls-position="right"
                  />
                  <div class="form-inline-tip">
                    第一阶段最多保留多少个候选进入证据重排。
                  </div>
                </el-form-item>
              </el-col>
              <el-col :xs="24" :md="12">
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
                    第一和第二候选分差不超过该值时标记为高歧义。
                  </div>
                </el-form-item>
              </el-col>
            </el-row>
            <el-alert
              type="info"
              :closable="false"
              show-icon
              title="默认策略优先快速预览；高级参数会影响候选范围、自动通过和 AI 调用量。"
            />
          </div>
        </el-collapse-transition>
      </el-form>
    </div>

    <!-- 高级选项 -->
    <div class="config-section">
      <button
        type="button"
        class="section-header section-header--button"
        :aria-expanded="showAdvanced"
        aria-controls="llm-review-options"
        @click="toggleLlmAdvanced"
      >
        <span>
          <span class="section-title">同步 LLM 复核</span>
          <small>等价裁决、调用预算与并行控制</small>
        </span>
        <el-icon :class="{ rotated: showAdvanced }">
          <ArrowRight />
        </el-icon>
      </button>

      <el-collapse-transition>
        <div
          v-show="showAdvanced"
          id="llm-review-options"
          class="advanced-options"
        >
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
              <el-col :xs="24" :md="8">
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
              <el-col :xs="24" :md="16">
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
              <el-col :xs="24" :md="8">
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
              <el-col :xs="24" :md="16">
                <span class="parallelism-hint">
                  LLM 判定等价但自评置信度低于此值时转人工确认；设为 0
                  表示不设门槛。语义优先模式下，该值是覆盖硬冲突的关键护栏。
                </span>
              </el-col>
            </el-row>
            <!-- LLM并行度 -->
            <el-row :gutter="20" align="middle">
              <el-col :xs="24" :md="8">
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
              <el-col :xs="24" :md="16">
                <span class="parallelism-hint">
                  同时处理的行数，值越大速度越快但占用资源越多；本地 Ollama 建议
                  1-4
                </span>
              </el-col>
            </el-row>
            <el-row :gutter="20" align="middle">
              <el-col :xs="24" :md="8">
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
              <el-col :xs="24" :md="16">
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
@media (width <= 720px) {
  .matching-strategy-summary {
    align-items: flex-start;
  }

  .matching-strategy-summary__metrics {
    flex-direction: column;
    gap: 2px;
    text-align: right;
  }

  .parallelism-hint {
    line-height: 1.6;
  }
}

@media (width <= 520px) {
  .matching-strategy-summary {
    flex-direction: column;
  }

  .matching-strategy-summary__metrics {
    text-align: left;
  }
}

@media (prefers-reduced-motion: reduce) {
  .section-header--button,
  .section-header .el-icon {
    transition: none;
  }
}

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

.section-header--button {
  gap: 16px;
  width: 100%;
  padding: 10px 12px;
  font: inherit;
  color: var(--color-text);
  text-align: left;
  background: transparent;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
  transition:
    color 180ms ease,
    background-color 180ms ease,
    border-color 180ms ease;
}

.section-header--button:hover {
  color: var(--app-primary);
  background: var(--el-fill-color-extra-light);
  border-color: var(--el-color-primary-light-5);
}

.section-header--button:focus-visible {
  outline: 2px solid var(--app-primary);
  outline-offset: 2px;
}

.section-header--button > span {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.section-header--button .section-title {
  margin-bottom: 0;
}

.section-header--button small {
  font-size: 12px;
  font-weight: 400;
  color: var(--app-text-secondary);
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
  scroll-margin-top: 112px;
  scroll-margin-bottom: calc(var(--smart-fill-action-bar-height, 72px) + 16px);
}

.matching-strategy-summary {
  display: flex;
  gap: 16px;
  align-items: center;
  justify-content: space-between;
  padding: 11px 13px;
  margin: 2px 0 10px;
  background: var(--el-color-primary-light-9);
  border: 1px solid var(--el-color-primary-light-7);
  border-radius: 8px;
}

.matching-strategy-summary > div:first-child {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.matching-strategy-summary span {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.matching-strategy-summary strong {
  font-size: 13px;
  font-weight: 650;
  color: var(--app-text-primary);
}

.matching-strategy-summary__metrics {
  display: flex;
  flex-wrap: wrap;
  gap: 6px 14px;
  justify-content: flex-end;
}

.matching-advanced-options {
  padding: 14px 14px 12px;
  margin-top: 10px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}

.matching-advanced-options .form-inline-tip {
  width: 100%;
  margin-left: 0;
  line-height: 1.5;
}

.service-status-alert {
  margin-top: 12px;
}

.automatic-service {
  display: flex;
  gap: 8px;
  align-items: center;
  width: 100%;
  max-width: 400px;
  min-height: 40px;
  padding: 8px 11px;
  background: var(--el-fill-color-extra-light);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 8px;
}

.automatic-service span,
.automatic-service small {
  font-size: 12px;
  color: var(--app-text-secondary);
}

.automatic-service strong {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 13px;
  color: var(--app-text-primary);
  white-space: nowrap;
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
