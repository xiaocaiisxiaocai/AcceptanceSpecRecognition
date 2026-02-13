<script setup lang="ts">
import { ref, watch } from "vue";
import { type MatchConfig, defaultMatchConfig } from "@/api/matching";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import { getAiServiceList, AiServicePurpose, type AiServiceConfig } from "@/api/ai-service";
import { ElMessage } from "element-plus";

const props = defineProps<{
  modelValue?: MatchConfig;
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

// 高级选项展开
const showAdvanced = ref(false);

// 同步modelValue
watch(
  () => props.modelValue,
  (val) => {
    if (val) {
      config.value = { ...defaultMatchConfig, ...val };
    } else {
      config.value = { ...defaultMatchConfig };
    }
  },
  { immediate: true }
);

// 触发配置更新
const updateConfig = () => {
  emit("update:modelValue", { ...config.value });
};

watch(config, updateConfig, { deep: true });

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
      <el-form label-width="120px">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="Embedding 服务">
              <el-select
                v-model="config.embeddingServiceId"
                placeholder="自动选择（离线优先）"
                clearable
                :loading="loadingAiServices"
                class="search-select search-select--240"
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
          </el-col>
          <el-col :span="12">
            <el-form-item label="LLM 服务">
              <el-select
                v-model="config.llmServiceId"
                placeholder="自动选择（离线优先）"
                clearable
                :loading="loadingAiServices"
                class="search-select search-select--240"
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
          </el-col>
        </el-row>
        <el-row :gutter="20">
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
            </el-form-item>
          </el-col>
        </el-row>
      </el-form>
    </div>

    <!-- 高级选项 -->
    <div class="config-section">
      <div class="section-header" @click="showAdvanced = !showAdvanced">
        <span class="section-title">LLM 复核与生成</span>
        <el-icon :class="{ rotated: showAdvanced }">
          <ArrowRight />
        </el-icon>
      </div>

      <el-collapse-transition>
        <div v-show="showAdvanced" class="advanced-options">
          <el-form label-width="140px">
            <!-- LLM复核 -->
            <el-row :gutter="20" align="middle" class="llm-row">
              <el-col :span="8">
                <el-form-item label="LLM复核">
                  <el-switch v-model="config.useLlmReview" />
                </el-form-item>
              </el-col>
            </el-row>

            <!-- LLM生成建议 -->
            <el-row :gutter="20" align="middle">
              <el-col :span="8">
                <el-form-item label="LLM生成建议">
                  <el-switch v-model="config.useLlmSuggestion" />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <el-form-item label="触发阈值">
                  <el-slider
                    v-model="config.llmSuggestionScoreThreshold"
                    :min="0"
                    :max="1"
                    :step="0.05"
                    :disabled="!config.useLlmSuggestion"
                    :format-tooltip="(val: number) => `${(val * 100).toFixed(0)}%`"
                  />
                </el-form-item>
              </el-col>
            </el-row>
            <div class="llm-hint">LLM 需要配置可用服务与模型，失败将返回明确原因。</div>

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

.reset-btn {
  text-align: right;
  margin-top: 12px;
}
</style>
