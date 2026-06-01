import { ref, computed } from "vue";
import type { Ref } from "vue";
import { ElMessage } from "element-plus";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import {
  getAiServiceList,
  AiServicePurpose,
  sortAiServicesByPriority,
  type AiServiceConfig
} from "@/api/ai-service";
import type { ImportDuplicateAiConfig } from "../dataImport.types";

/**
 * 导入目标选择逻辑（客户、制程、机型、AI 服务）
 */
export function useDataImportTarget(
  importDuplicateAiConfig: Ref<ImportDuplicateAiConfig>
) {
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

  const selectedMachineModelName = computed(() => {
    if (!selectedMachineModelId.value) return "-";
    return (
      machineModels.value.find(
        model => model.id === selectedMachineModelId.value
      )?.name ?? `机型#${selectedMachineModelId.value}`
    );
  });

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

  const loadAiServices = async () => {
    loadingAiServices.value = true;
    try {
      const res = await getAiServiceList({ page: 1, pageSize: 200 });
      if (res.code === 0) {
        const items = res.data.items || [];
        const enabledItems = items.filter(item => !item.isDisabled);
        embeddingServices.value = sortAiServicesByPriority(
          enabledItems.filter(
            item =>
              (item.purpose & AiServicePurpose.Embedding) ===
                AiServicePurpose.Embedding && !!item.embeddingModel
          )
        );
        llmServices.value = sortAiServicesByPriority(
          enabledItems.filter(
            item =>
              (item.purpose & AiServicePurpose.Llm) ===
                AiServicePurpose.Llm && !!item.llmModel
          )
        );

        if (
          importDuplicateAiConfig.value.embeddingServiceId &&
          !embeddingServices.value.some(
            s => s.id === importDuplicateAiConfig.value.embeddingServiceId
          )
        ) {
          importDuplicateAiConfig.value.embeddingServiceId = undefined;
        }
        if (
          importDuplicateAiConfig.value.llmServiceId &&
          !llmServices.value.some(
            s => s.id === importDuplicateAiConfig.value.llmServiceId
          )
        ) {
          importDuplicateAiConfig.value.llmServiceId = undefined;
        }
        if (
          !importDuplicateAiConfig.value.embeddingServiceId &&
          embeddingServices.value.length > 0
        ) {
          importDuplicateAiConfig.value.embeddingServiceId =
            embeddingServices.value[0].id;
        }
        if (
          !importDuplicateAiConfig.value.llmServiceId &&
          llmServices.value.length > 0
        ) {
          importDuplicateAiConfig.value.llmServiceId =
            llmServices.value[0].id;
        }
        return;
      }
      ElMessage.error(res.message || "加载 AI 服务失败");
    } catch {
      ElMessage.error("加载 AI 服务失败");
    } finally {
      loadingAiServices.value = false;
    }
  };

  /** 重置目标选择状态 */
  const resetTargetSelection = () => {
    selectedCustomerId.value = undefined;
    selectedProcessId.value = undefined;
    selectedMachineModelId.value = undefined;
  };

  return {
    customers,
    processes,
    machineModels,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    selectedMachineModelName,
    loadingCustomers,
    loadingProcesses,
    loadingMachineModels,
    loadingAiServices,
    embeddingServices,
    llmServices,
    loadCustomers,
    loadProcesses,
    loadMachineModels,
    loadAiServices,
    resetTargetSelection
  };
}
