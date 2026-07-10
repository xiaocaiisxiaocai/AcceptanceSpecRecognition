import { ref, computed, onScopeDispose } from "vue";
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
import { getRequestErrorMessage } from "@/utils/error-message";
import { loadAllPagedItems } from "@/utils/paged-options";

type DataImportTargetSelectionRefs = {
  selectedCustomerId: Ref<number | undefined>;
  selectedProcessId: Ref<number | undefined>;
  selectedMachineModelId: Ref<number | undefined>;
};

/**
 * 导入目标选择逻辑（客户、制程、机型、AI 服务）
 */
export function useDataImportTarget(
  importDuplicateAiConfig: Ref<ImportDuplicateAiConfig>,
  targetSelectionRefs?: DataImportTargetSelectionRefs
) {
  const customers = ref<Customer[]>([]);
  const processes = ref<Process[]>([]);
  const machineModels = ref<MachineModel[]>([]);
  const selectedCustomerId =
    targetSelectionRefs?.selectedCustomerId ??
    ref<number | undefined>(undefined);
  const selectedProcessId =
    targetSelectionRefs?.selectedProcessId ??
    ref<number | undefined>(undefined);
  const selectedMachineModelId =
    targetSelectionRefs?.selectedMachineModelId ??
    ref<number | undefined>(undefined);
  const loadingCustomers = ref(false);
  const loadingProcesses = ref(false);
  const loadingMachineModels = ref(false);
  const loadingAiServices = ref(false);
  let customerOptionsController: AbortController | undefined;
  let processOptionsController: AbortController | undefined;
  let machineModelOptionsController: AbortController | undefined;
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
              (item.purpose & AiServicePurpose.Llm) === AiServicePurpose.Llm &&
              !!item.llmModel
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
          importDuplicateAiConfig.value.llmServiceId = llmServices.value[0].id;
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

  onScopeDispose(() => {
    customerOptionsController?.abort();
    processOptionsController?.abort();
    machineModelOptionsController?.abort();
  });

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
