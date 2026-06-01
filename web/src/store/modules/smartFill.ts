import { defineStore } from "pinia";
import { ref, computed } from "vue";
import { store } from "../utils";
import type { FileUploadResponse, TableInfo } from "@/api/document";
import type {
  MatchConfig,
  BatchTablePreviewResult,
  BatchPreviewProgressResponse,
  BatchTableConfig
} from "@/api/matching";
import { defaultMatchConfig } from "@/api/matching";

/** 批量表格配置项（含运行时状态） */
interface BatchTableConfigItem extends BatchTableConfig {
  tableIndex: number;
  tableName?: string;
}

/**
 * 智能填充工作流状态管理
 * 用于跨组件共享匹配填充流程的核心状态
 */
export const useSmartFillStore = defineStore("smartFill", () => {
  // 步骤
  const currentStep = ref(0);

  // 文件
  const uploadedFile = ref<FileUploadResponse | null>(null);
  const isExcelFile = computed(() => uploadedFile.value?.fileType === 1);
  const allTables = ref<TableInfo[]>([]);

  // 表格配置
  const batchTableConfigs = ref<BatchTableConfigItem[]>([]);

  // 匹配配置
  const matchConfig = ref<MatchConfig>({ ...defaultMatchConfig });

  // 预览结果
  const batchPreviewResults = ref<BatchTablePreviewResult[]>([]);
  const loading = ref(false);
  const previewProgress = ref<BatchPreviewProgressResponse | null>(null);

  // 预览状态
  type PreviewState = "none" | "previewing" | "done" | "failed";
  const previewState = ref<PreviewState>("none");

  /** 是否有预览结果 */
  const hasPreviewResults = computed(
    () => batchPreviewResults.value.length > 0
  );

  /** 预览总行数 */
  const totalPreviewRows = computed(() =>
    batchPreviewResults.value.reduce(
      (sum, table) => sum + table.items.length,
      0
    )
  );

  /** 重置整个填充流程状态 */
  function resetAll() {
    currentStep.value = 0;
    uploadedFile.value = null;
    allTables.value = [];
    batchTableConfigs.value = [];
    matchConfig.value = { ...defaultMatchConfig };
    batchPreviewResults.value = [];
    loading.value = false;
    previewProgress.value = null;
    previewState.value = "none";
  }

  return {
    currentStep,
    uploadedFile,
    isExcelFile,
    allTables,
    batchTableConfigs,
    matchConfig,
    batchPreviewResults,
    loading,
    previewProgress,
    previewState,
    hasPreviewResults,
    totalPreviewRows,
    resetAll
  };
});

export function useSmartFillStoreHook() {
  return useSmartFillStore(store);
}
