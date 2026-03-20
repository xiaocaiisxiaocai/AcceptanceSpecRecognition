<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  type MatchConfig,
  defaultMatchConfig,
  MatchingStrategy
} from "@/api/matching";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import { getAiServiceList, AiServicePurpose, type AiServiceConfig } from "@/api/ai-service";
import { ElMessage } from "element-plus";

const props = defineProps<{
  modelValue?: MatchConfig;
  allowLlm?: boolean;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: MatchConfig): void;
  (e: "scopeChange", customerId?: number, processId?: number, machineModelId?: number): void;
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
const loadingCustomers = ref(false);
const loadingProcesses = ref(false);
const loadingMachineModels = ref(false);
const loadingAiServices = ref(false);
const embeddingServices = ref<AiServiceConfig[]>([]);
const llmServices = ref<AiServiceConfig[]>([]);
const allowLlm = computed(() => props.allowLlm !== false);
const strategyOptions = [
  {
    value: MatchingStrategy.SingleStage,
    label: "基础方式",
    description: "只按 Embedding Top1 返回，速度更快，但复杂模板更容易漏匹配。"
  },
  {
    value: MatchingStrategy.MultiStage,
    label: "多阶段重排",
    description: "先召回 TopK，再按项目/规格规则重排，适合作为默认推荐。"
  }
] as const;

// 高级选项展开
const showAdvanced = ref(false);

// 标记：正在从内部更新到外部，避免回写时触发整体替换
let isInternalUpdate = false;

// 同步 modelValue → config（仅在外部驱动时逐属性更新，避免整体替换导致 el-select 失去选中状态）
watch(
  () => props.modelValue,
  (val) => {
    if (isInternalUpdate) return;
    const source = { ...defaultMatchConfig, ...val };
    const keys = Object.keys(source) as (keyof typeof source)[];
    for (const key of keys) {
      if ((config.value as any)[key] !== (source as any)[key]) {
        (config.value as any)[key] = (source as any)[key];
      }
    }
    if (!allowLlm.value) {
      config.value.useLlmReview = false;
      config.value.useLlmSuggestion = false;
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
  allowLlm,
  enabled => {
    if (!enabled) {
      config.value.useLlmReview = false;
      config.value.useLlmSuggestion = false;
    }
  },
  { immediate: true }
);

watch(
  () => config.value.matchingStrategy,
  (strategy) => {
    if (strategy === MatchingStrategy.MultiStage) {
      if (!config.value.recallTopK || config.value.recallTopK < 1) {
        config.value.recallTopK = defaultMatchConfig.recallTopK;
      }
      if (config.value.ambiguityMargin === undefined || config.value.ambiguityMargin === null) {
        config.value.ambiguityMargin = defaultMatchConfig.ambiguityMargin;
      }
    }
  },
  { immediate: true }
);

// 加载客户列表
const loadCustomers = async () => {
  loadingCustomers.value = true;
  try {
    const res = await getCustomerList({ page: 1, pageSize: 100 });
    if (res.code === 0) {
      customers.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载客户列表失败");
  } finally {
    loadingCustomers.value = false;
  }
};

// 加载制程列表
const loadProcesses = async () => {
  loadingProcesses.value = true;
  try {
    const res = await getProcessList({ page: 1, pageSize: 1000 });
    if (res.code === 0) {
      processes.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载制程列表失败");
  } finally {
    loadingProcesses.value = false;
  }
};

// 加载机型列表
const loadMachineModels = async () => {
  loadingMachineModels.value = true;
  try {
    const res = await getMachineModelList({ page: 1, pageSize: 1000 });
    if (res.code === 0) {
      machineModels.value = res.data.items;
    }
  } catch {
    ElMessage.error("加载机型列表失败");
  } finally {
    loadingMachineModels.value = false;
  }
};

// 加载 AI 服务列表
const loadAiServices = async () => {
  loadingAiServices.value = true;
  try {
    const res = await getAiServiceList({ page: 1, pageSize: 200 });
    if (res.code === 0) {
      const items = res.data.items;
      embeddingServices.value = items.filter(
        (s) =>
          (s.purpose & AiServicePurpose.Embedding) === AiServicePurpose.Embedding &&
          !!s.embeddingModel
      );
      llmServices.value = items.filter(
        (s) =>
          (s.purpose & AiServicePurpose.Llm) === AiServicePurpose.Llm &&
          !!s.llmModel
      );
      // 自动选择第一个可用服务（如果尚未选择）
      if (!config.value.embeddingServiceId && embeddingServices.value.length > 0) {
        config.value.embeddingServiceId = embeddingServices.value[0].id;
      }
      if (!config.value.llmServiceId && llmServices.value.length > 0) {
        config.value.llmServiceId = llmServices.value[0].id;
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

// 监听客户变化
watch(selectedCustomerId, () => {
  emit("scopeChange", selectedCustomerId.value, selectedProcessId.value, selectedMachineModelId.value);
});

// 监听制程变化
watch(selectedProcessId, () => {
  emit("scopeChange", selectedCustomerId.value, selectedProcessId.value, selectedMachineModelId.value);
});

// 监听机型变化
watch(selectedMachineModelId, () => {
  emit("scopeChange", selectedCustomerId.value, selectedProcessId.value, selectedMachineModelId.value);
});

// 重置配置
const resetConfig = () => {
  config.value = { ...defaultMatchConfig };
  if (!allowLlm.value) {
    config.value.useLlmReview = false;
    config.value.useLlmSuggestion = false;
  }
};

// 初始化
loadCustomers();
loadProcesses();
loadMachineModels();
loadAiServices();

// 暴露方法
defineExpose({
  resetConfig,
  getScope: () => ({
    customerId: selectedCustomerId.value,
    processId: selectedProcessId.value,
    machineModelId: selectedMachineModelId.value
  })
});
</script>

<template>
  <div class="match-config">
    <!-- 匹配范围 -->
    <div class="config-section">
      <div class="section-title">匹配范围</div>
      <el-form :inline="true" class="scope-form">
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
        </el-form-item>
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="匹配策略">
              <el-radio-group v-model="config.matchingStrategy">
                <el-radio-button
                  v-for="option in strategyOptions"
                  :key="option.value"
                  :value="option.value"
                >
                  {{ option.label }}
                </el-radio-button>
              </el-radio-group>
              <div class="strategy-tip">
                {{
                  strategyOptions.find(
                    (option) => option.value === config.matchingStrategy
                  )?.description
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
                该阈值用于保留候选，自动勾选仍以高置信阈值为准
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
                达到该阈值的匹配会默认选中；默认值为 95%
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
        </el-row>
        <el-row
          v-if="config.matchingStrategy === MatchingStrategy.MultiStage"
          :gutter="20"
        >
          <el-col :span="12">
            <el-form-item label="召回候选数">
              <el-input-number
                v-model="config.recallTopK"
                :min="1"
                :max="20"
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
          :title="`系统仅自动填充匹配得分大于等于 ${((config.highConfidenceThreshold ?? 0.95) * 100).toFixed(0)}% 的结果；其余命中只做 LLM 复核，不会生成新验收标准写回。`"
        />
      </el-form>
    </div>

    <!-- 高级选项 -->
    <div class="config-section">
      <div class="section-header" @click="showAdvanced = !showAdvanced">
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
            <!-- LLM复核 -->
            <el-row :gutter="20" align="middle" class="llm-row">
              <el-col :span="8">
                <el-form-item label="LLM复核">
                  <el-switch v-model="config.useLlmReview" :disabled="!allowLlm" />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  仅对未达到当前高置信阈值的匹配结果进行语义复核，通过后才允许采用已有规格
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
                    :disabled="!allowLlm || !config.useLlmReview"
                    size="default"
                    controls-position="right"
                  />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <span class="parallelism-hint">
                  同时处理的行数，值越大速度越快但占用资源越多
                </span>
              </el-col>
            </el-row>
            <div class="llm-hint">
              LLM 仅负责复核“是否可直接采用现有规格”，不会生成新验收标准参与落库。
            </div>

            <div class="reset-btn">
              <el-button size="small" @click="resetConfig">重置为默认值</el-button>
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
  margin-bottom: 20px;
  padding-bottom: 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.config-section:last-child {
  border-bottom: none;
  margin-bottom: 0;
}

.section-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--color-text);
  margin-bottom: 12px;
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
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: #6b7280;
}

.advanced-options {
  padding-top: 16px;
}

.llm-row {
  margin-top: 8px;
}

.llm-hint {
  margin-top: 4px;
  font-size: 12px;
  color: #6b7280;
}

.parallelism-hint {
  font-size: 12px;
  color: #9ca3af;
  line-height: 32px;
}

.form-inline-tip {
  margin-left: 8px;
  font-size: 12px;
  color: #9ca3af;
}

.strategy-tip {
  margin-top: 8px;
  font-size: 12px;
  color: #6b7280;
}

.reset-btn {
  text-align: right;
  margin-top: 12px;
}
</style>
