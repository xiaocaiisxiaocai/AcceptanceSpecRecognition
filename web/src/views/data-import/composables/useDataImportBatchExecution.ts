import { computed, type ComputedRef, type Ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  importData,
  importExcelData,
  type ExcelImportDataRequest,
  type FileUploadResponse,
  type ImportDuplicateCheckOptions
} from "@/api/document";
import { ensurePermission } from "@/utils/permission-guard";
import {
  defaultExcelMapping,
  normalizeExcelMappingByTable
} from "../dataImport.helpers";
import {
  buildEmptyImportAggregate,
  createSingleTableAggregate,
  mergeImportAggregates,
  splitBatchAggregates
} from "../dataImport.execution.helpers";
import type {
  CombinedImportResult,
  DifferenceDecision,
  ImportBatchExecutionResult,
  ImportDuplicateAiConfig,
  ImportPendingDifferenceWithTable,
  TableImportConfig
} from "../dataImport.types";

type UseDataImportBatchExecutionOptions = {
  isExcelFile: ComputedRef<boolean>;
  uploadedFile: Ref<FileUploadResponse | null>;
  tableConfigs: Ref<TableImportConfig[]>;
  selectedCustomerId: Ref<number | undefined>;
  selectedProcessId: Ref<number | undefined>;
  selectedMachineModelId: Ref<number | undefined>;
  importing: Ref<boolean>;
  importResult: Ref<CombinedImportResult | null>;
  pendingImportAggregate: Ref<CombinedImportResult | null>;
  committedImportAggregate: Ref<CombinedImportResult | null>;
  previewSkippedRows: Ref<boolean>;
  differenceDecisionMap: Ref<Record<string, DifferenceDecision | undefined>>;
  differenceConfirmDialogVisible: Ref<boolean>;
  importDuplicateAiConfig: Ref<ImportDuplicateAiConfig>;
  importProgressText: Ref<string>;
  pendingDifferences: ComputedRef<ImportPendingDifferenceWithTable[]>;
  pendingUndecidedCount: ComputedRef<number>;
  pendingTableIndexes: ComputedRef<number[]>;
  previewDataCount: ComputedRef<number>;
  hasPendingDifferenceConfirmation: ComputedRef<boolean>;
  currentImportPermissionCode: ComputedRef<string>;
  currentImportPermissionMessage: ComputedRef<string>;
  getExcludedRowIndexes: (tableIndex: number) => number[];
  resetPendingDifferenceState: () => void;
  syncDifferenceDecisionMap: (
    items: ImportPendingDifferenceWithTable[]
  ) => void;
};

type CompleteExcelImportMapping = Pick<
  ExcelImportDataRequest,
  | "headerRowStart"
  | "headerRowCount"
  | "dataStartRow"
  | "dataEndRow"
  | "specificationColumn"
  | "acceptanceColumn"
  | "remarkColumn"
> & {
  projectColumn?: number;
};

export function useDataImportBatchExecution(
  options: UseDataImportBatchExecutionOptions
) {
  const buildDuplicateCheckOptions = (): ImportDuplicateCheckOptions => {
    const config = options.importDuplicateAiConfig.value;
    return {
      enableSemanticDuplicateCheck: config.enableSemanticDuplicateCheck,
      embeddingServiceId: config.enableSemanticDuplicateCheck
        ? config.embeddingServiceId
        : undefined,
      semanticTopK: config.semanticTopK,
      semanticMinScore: config.semanticMinScore,
      enableLlmDuplicateReview:
        config.enableSemanticDuplicateCheck && config.enableLlmDuplicateReview,
      llmServiceId:
        config.enableSemanticDuplicateCheck && config.enableLlmDuplicateReview
          ? config.llmServiceId
          : undefined,
      llmPassScore: config.llmPassScore,
      highConfidenceThreshold: config.highConfidenceThreshold
    };
  };

  const validateDuplicateAiConfig = () => {
    if (!options.importDuplicateAiConfig.value.enableSemanticDuplicateCheck) {
      return true;
    }

    if (!options.importDuplicateAiConfig.value.embeddingServiceId) {
      ElMessage.warning("已启用 AI 疑似重复识别，请先选择 Embedding 服务");
      return false;
    }

    if (
      options.importDuplicateAiConfig.value.enableLlmDuplicateReview &&
      !options.importDuplicateAiConfig.value.llmServiceId
    ) {
      ElMessage.warning("已启用 LLM 复核，请先选择 LLM 服务");
      return false;
    }

    return true;
  };

  const buildDifferenceKeysByTable = (tableIndex: number) => {
    const confirmed: string[] = [];
    const partial: string[] = [];
    const skipped: string[] = [];
    for (const item of options.pendingDifferences.value) {
      if (item.tableIndex !== tableIndex) continue;
      const decision = options.differenceDecisionMap.value[item.key];
      if (decision === "import") confirmed.push(item.key);
      if (decision === "partial") partial.push(item.key);
      if (decision === "skip") skipped.push(item.key);
    }
    return { confirmed, partial, skipped };
  };

  const buildImportProgressText = (
    cfg: TableImportConfig,
    currentIndex: number,
    total: number,
    includeDifferenceDecisions: boolean,
    duplicateCheckOptions: ImportDuplicateCheckOptions
  ) => {
    const sourceLabel = `${options.isExcelFile.value ? "工作表" : "表格"} ${cfg.tableIndex + 1}`;
    const actionLabel = includeDifferenceDecisions
      ? "正在按确认结果继续导入"
      : "正在导入";

    if (!duplicateCheckOptions.enableSemanticDuplicateCheck) {
      return `${actionLabel}${sourceLabel}（${currentIndex}/${total}）`;
    }

    if (duplicateCheckOptions.enableLlmDuplicateReview) {
      return `${actionLabel}${sourceLabel}（${currentIndex}/${total}），执行 Embedding 召回与 LLM 复核中`;
    }

    return `${actionLabel}${sourceLabel}（${currentIndex}/${total}），执行 Embedding 疑似重复识别中`;
  };

  const clearImportProgress = () => {
    options.importProgressText.value = "";
  };

  const executeImportBatch = async (
    configs: TableImportConfig[],
    includeDifferenceDecisions: boolean
  ): Promise<ImportBatchExecutionResult> => {
    const tableAggregates: CombinedImportResult[] = [];
    let hasPendingEncountered = false;
    const duplicateCheckOptions = buildDuplicateCheckOptions();

    for (const [idx, cfg] of configs.entries()) {
      options.importProgressText.value = buildImportProgressText(
        cfg,
        idx + 1,
        configs.length,
        includeDifferenceDecisions,
        duplicateCheckOptions
      );

      const cleanupSourceFile =
        !hasPendingEncountered && idx === configs.length - 1;
      const fileId = options.uploadedFile.value?.fileId;
      const customerId = options.selectedCustomerId.value;
      if (fileId === undefined || customerId === undefined) {
        throw new Error("导入上下文已失效，请重新选择文件和目标客户");
      }

      const { confirmed, partial, skipped } = includeDifferenceDecisions
        ? buildDifferenceKeysByTable(cfg.tableIndex)
        : {
            confirmed: [] as string[],
            partial: [] as string[],
            skipped: [] as string[]
          };
      const excludedRowIndexes = options.getExcludedRowIndexes(cfg.tableIndex);
      const normalizedExcelMapping = normalizeExcelMappingByTable(
        cfg.tableInfo,
        cfg.excelMapping ?? defaultExcelMapping()
      );
      if (
        options.isExcelFile.value &&
        ((!cfg.isSpecificationOnly &&
          normalizedExcelMapping.projectColumn === undefined) ||
          normalizedExcelMapping.specificationColumn === undefined)
      ) {
        throw new Error(`工作表 ${cfg.tableIndex + 1} 缺少项目列或规格列映射`);
      }
      const excelImportMapping: CompleteExcelImportMapping = {
        ...normalizedExcelMapping,
        projectColumn: normalizedExcelMapping.projectColumn,
        specificationColumn: normalizedExcelMapping.specificationColumn ?? 0
      };

      const res = options.isExcelFile.value
        ? await importExcelData({
            ...excelImportMapping,
            fileId,
            sheetIndex: cfg.tableIndex,
            customerId,
            processId: options.selectedProcessId.value || undefined,
            machineModelId: options.selectedMachineModelId.value || undefined,
            cleanupSourceFile,
            previewSkippedRows: options.previewSkippedRows.value,
            confirmedDifferenceKeys: confirmed,
            partiallyConfirmedDifferenceKeys: partial,
            skippedDifferenceKeys: skipped,
            excludedRowIndexes,
            duplicateCheckOptions,
            isSpecificationOnly: cfg.isSpecificationOnly
          })
        : await importData({
            fileId,
            tableIndex: cfg.tableIndex,
            customerId,
            processId: options.selectedProcessId.value || undefined,
            machineModelId: options.selectedMachineModelId.value || undefined,
            cleanupSourceFile,
            previewSkippedRows: options.previewSkippedRows.value,
            confirmedDifferenceKeys: confirmed,
            partiallyConfirmedDifferenceKeys: partial,
            skippedDifferenceKeys: skipped,
            excludedRowIndexes,
            duplicateCheckOptions,
            isSpecificationOnly: cfg.isSpecificationOnly,
            mapping: cfg.wordMapping!
          });

      if (res.code !== 0) {
        tableAggregates.push({
          ...buildEmptyImportAggregate(),
          failedCount: 1,
          errors: [
            {
              tableIndex: cfg.tableIndex,
              rowIndex: 0,
              message: res.message || "导入失败"
            }
          ]
        });
        continue;
      }

      if (res.data.requiresConfirmation && (res.data.pendingCount || 0) > 0) {
        hasPendingEncountered = true;
      }

      tableAggregates.push(
        createSingleTableAggregate(cfg.tableIndex, res.data)
      );
    }

    return {
      aggregate: mergeImportAggregates(...tableAggregates),
      tableAggregates
    };
  };

  const openDifferenceConfirmDialog = () => {
    if (!options.pendingImportAggregate.value?.pendingDifferences.length)
      return;
    options.differenceConfirmDialogVisible.value = true;
  };

  const handleConfirmPendingDifferences = async () => {
    if (
      !options.pendingImportAggregate.value ||
      options.pendingDifferences.value.length === 0
    ) {
      return;
    }

    if (options.pendingUndecidedCount.value > 0) {
      ElMessage.warning(
        `请先逐条确认重复项（仍有 ${options.pendingUndecidedCount.value} 条未确认）`
      );
      return;
    }

    options.importing.value = true;
    // 先收起旧弹窗并清空旧提示，避免大批量确认时残留旧状态造成“重复确认循环”的错觉。
    options.differenceConfirmDialogVisible.value = false;
    ElMessage.closeAll();
    try {
      const previousCommittedAggregate = options.committedImportAggregate.value;
      const pendingSet = new Set(options.pendingTableIndexes.value);
      const pendingConfigs = options.tableConfigs.value.filter(cfg =>
        pendingSet.has(cfg.tableIndex)
      );
      options.importProgressText.value = `正在按确认结果继续导入 ${pendingConfigs.length} 个${options.isExcelFile.value ? "工作表" : "表格"}`;

      const batch = await executeImportBatch(pendingConfigs, true);
      const splitResult = splitBatchAggregates(batch.tableAggregates);

      if (splitResult.pending.pendingDifferences.length > 0) {
        options.committedImportAggregate.value = mergeImportAggregates(
          previousCommittedAggregate,
          splitResult.completed
        );
        options.pendingImportAggregate.value = splitResult.pending;
        options.syncDifferenceDecisionMap(
          splitResult.pending.pendingDifferences
        );
        options.differenceConfirmDialogVisible.value = true;
        ElMessage.closeAll();
        ElMessage.warning(
          `仍有 ${splitResult.pending.pendingCount || 0} 条重复项未确认`
        );
        return;
      }

      const finalAggregate = mergeImportAggregates(
        previousCommittedAggregate,
        batch.aggregate
      );

      options.importResult.value = finalAggregate;
      options.resetPendingDifferenceState();
      ElMessage.closeAll();
      ElMessage.success(
        `导入完成：成功${finalAggregate.successCount}条，失败${finalAggregate.failedCount}条`
      );
    } catch (error) {
      options.differenceConfirmDialogVisible.value = true;
      ElMessage.error(
        error instanceof Error ? error.message : "继续导入失败，请稍后重试"
      );
    } finally {
      options.importing.value = false;
      clearImportProgress();
    }
  };

  const handleImport = async () => {
    if (
      !options.uploadedFile.value ||
      options.tableConfigs.value.length === 0 ||
      !options.selectedCustomerId.value
    ) {
      return;
    }

    if (
      options.pendingImportAggregate.value &&
      options.pendingDifferences.value.length > 0
    ) {
      openDifferenceConfirmDialog();
      return;
    }

    if (options.previewDataCount.value <= 0) {
      ElMessage.warning("当前没有可导入的数据，请先恢复或重新选择待导入行");
      return;
    }

    if (!validateDuplicateAiConfig()) {
      return;
    }

    if (
      !ensurePermission(
        options.currentImportPermissionCode.value,
        options.currentImportPermissionMessage.value
      )
    ) {
      return;
    }

    try {
      await ElMessageBox.confirm(
        `确定要将 ${options.tableConfigs.value.length} 个${options.isExcelFile.value ? "工作表" : "表格"}的数据导入到所选客户/制程/机型吗？`,
        "确认导入",
        {
          confirmButtonText: "确定",
          cancelButtonText: "取消",
          type: "warning"
        }
      );

      options.importing.value = true;
      options.importProgressText.value = `正在准备导入 ${options.tableConfigs.value.length} 个${options.isExcelFile.value ? "工作表" : "表格"}`;
      const batch = await executeImportBatch(options.tableConfigs.value, false);
      const splitResult = splitBatchAggregates(batch.tableAggregates);

      if (splitResult.pending.pendingDifferences.length > 0) {
        options.importResult.value = null;
        options.committedImportAggregate.value = splitResult.completed;
        options.pendingImportAggregate.value = splitResult.pending;
        options.syncDifferenceDecisionMap(
          splitResult.pending.pendingDifferences
        );
        options.differenceConfirmDialogVisible.value = true;
        ElMessage.closeAll();
        ElMessage.warning(
          `检测到 ${splitResult.pending.pendingCount || 0} 条重复、差异或 AI 疑似重复数据，请在弹窗中逐条确认是否覆盖已有记录`
        );
        return;
      }

      options.importResult.value = batch.aggregate;
      options.resetPendingDifferenceState();
      ElMessage.closeAll();
      ElMessage.success(
        `导入完成：成功${batch.aggregate.successCount}条，失败${batch.aggregate.failedCount}条`
      );
    } catch (error) {
      if (error === "cancel" || error === "close") {
        return;
      }

      ElMessage.error(
        error instanceof Error ? error.message : "导入失败，请稍后重试"
      );
    } finally {
      options.importing.value = false;
      clearImportProgress();
    }
  };

  const importProgressDescription = computed(() => {
    if (!options.importing.value) {
      return "";
    }

    const currentOptions = buildDuplicateCheckOptions();
    if (!currentOptions.enableSemanticDuplicateCheck) {
      return "系统正在提交并写入导入结果，请耐心等待，不要关闭页面或刷新浏览器。";
    }

    if (currentOptions.enableLlmDuplicateReview) {
      return "当前已启用 Embedding 召回与 LLM 复核，处理时间会明显高于普通导入，请耐心等待，不要关闭页面或刷新浏览器。";
    }

    return "当前已启用 Embedding 疑似重复识别，系统会先分析候选再继续导入，请耐心等待，不要关闭页面或刷新浏览器。";
  });

  const importPrimaryButtonText = computed(() => {
    if (options.importing.value) {
      return options.importProgressText.value || "正在导入...";
    }

    return options.hasPendingDifferenceConfirmation.value
      ? "继续处理重复项"
      : "开始导入";
  });

  const confirmDifferenceButtonText = computed(() => {
    return options.importing.value
      ? importPrimaryButtonText.value
      : "确认并继续导入";
  });

  return {
    buildDuplicateCheckOptions,
    validateDuplicateAiConfig,
    executeImportBatch,
    buildImportProgressText,
    clearImportProgress,
    openDifferenceConfirmDialog,
    handleConfirmPendingDifferences,
    handleImport,
    importProgressDescription,
    importPrimaryButtonText,
    confirmDifferenceButtonText
  };
}
