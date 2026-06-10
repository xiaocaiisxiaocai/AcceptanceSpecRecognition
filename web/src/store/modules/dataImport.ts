import { defineStore } from "pinia";
import { ref, computed } from "vue";
import { store } from "../utils";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import type {
  TableImportConfig,
  ImportDuplicateAiConfig
} from "@/views/data-import/dataImport.types";
import { createDefaultImportDuplicateAiConfig } from "@/views/data-import/composables/useDataImportExecution";

/**
 * 数据导入工作流状态管理
 * 用于跨组件共享导入流程的核心状态
 */
export const useDataImportStore = defineStore("dataImport", () => {
  // 步骤
  const currentStep = ref(0);

  // 文件
  const uploadedFile = ref<FileUploadResponse | null>(null);
  const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);

  // 表格选择
  const selectedTableIndexes = ref<number[]>([]);
  const selectedTables = ref<TableInfo[]>([]);
  const activeTableIndex = ref<number | null>(null);
  const tableConfigs = ref<TableImportConfig[]>([]);

  // 目标选择
  const selectedCustomerId = ref<number | undefined>(undefined);
  const selectedProcessId = ref<number | undefined>(undefined);
  const selectedMachineModelId = ref<number | undefined>(undefined);

  // AI 配置
  const importDuplicateAiConfig = ref<ImportDuplicateAiConfig>(
    createDefaultImportDuplicateAiConfig()
  );

  /** 重置整个导入流程状态 */
  function resetAll() {
    currentStep.value = 0;
    uploadedFile.value = null;
    selectedTableIndexes.value = [];
    selectedTables.value = [];
    activeTableIndex.value = null;
    tableConfigs.value = [];
    selectedCustomerId.value = undefined;
    selectedProcessId.value = undefined;
    selectedMachineModelId.value = undefined;
    importDuplicateAiConfig.value = createDefaultImportDuplicateAiConfig();
  }

  return {
    currentStep,
    uploadedFile,
    isExcelFile,
    selectedTableIndexes,
    selectedTables,
    activeTableIndex,
    tableConfigs,
    selectedCustomerId,
    selectedProcessId,
    selectedMachineModelId,
    importDuplicateAiConfig,
    resetAll
  };
});

export function useDataImportStoreHook() {
  return useDataImportStore(store);
}
