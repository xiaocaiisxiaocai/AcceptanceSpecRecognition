import { ref, computed, onDeactivated, onScopeDispose } from "vue";
import type { Ref } from "vue";
import { ElMessage } from "element-plus";
import { getCustomerList, type Customer } from "@/api/customer";
import { getProcessList, type Process } from "@/api/process";
import { getMachineModelList, type MachineModel } from "@/api/machine-model";
import type { AiServiceSelection } from "@/api/ai-service";
import type { ImportDuplicateAiConfig } from "../dataImport.types";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";
import { loadAllPagedItems } from "@/utils/paged-options";
import { createAiSelectionRetryController } from "@/utils/ai-selection-retry";
import {
  getRuntimeAiPurposeResult,
  loadRuntimeAiSelectionsSettled,
  type RuntimeAiSelectionRefreshResult
} from "@/utils/runtime-ai-selection-loader";
import { applyDataImportRuntimeAiSelections } from "./data-import-ai-selection";

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
  let aiSelectionController: AbortController | undefined;
  let aiSelectionVersion = 0;
  let aiSelectionRequest: Promise<RuntimeAiSelectionRefreshResult> | undefined;
  const embeddingSelection = ref<AiServiceSelection>({ status: "checking" });
  const llmSelection = ref<AiServiceSelection>({ status: "checking" });

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
      if (!controller.signal.aborted && !isGloballyHandledAuthError(error)) {
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
      if (!controller.signal.aborted && !isGloballyHandledAuthError(error)) {
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
      if (!controller.signal.aborted && !isGloballyHandledAuthError(error)) {
        ElMessage.error(getRequestErrorMessage(error, "加载机型列表失败"));
      }
    } finally {
      if (machineModelOptionsController === controller) {
        machineModelOptionsController = undefined;
        loadingMachineModels.value = false;
      }
    }
  };

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
      applyDataImportRuntimeAiSelections(
        importDuplicateAiConfig.value,
        embeddingResult.selection,
        llmResult.selection
      );
      aiSelectionRetry.schedule([
        embeddingResult.selection,
        llmResult.selection
      ]);

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

  /** 重置目标选择状态 */
  const resetTargetSelection = () => {
    selectedCustomerId.value = undefined;
    selectedProcessId.value = undefined;
    selectedMachineModelId.value = undefined;
  };

  const stopAiSelectionRequests = () => {
    aiSelectionVersion += 1;
    aiSelectionController?.abort();
    aiSelectionRequest = undefined;
    aiSelectionRetry.cancel();
  };

  onDeactivated(stopAiSelectionRequests);
  onScopeDispose(() => {
    customerOptionsController?.abort();
    processOptionsController?.abort();
    machineModelOptionsController?.abort();
    stopAiSelectionRequests();
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
    embeddingSelection,
    llmSelection,
    loadCustomers,
    loadProcesses,
    loadMachineModels,
    loadAiServices,
    resetTargetSelection
  };
}
