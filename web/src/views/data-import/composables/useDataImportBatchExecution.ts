import { computed, watch, type ComputedRef, type Ref } from "vue";
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
  normalizeExcelMappingByTable,
  shouldBackfillProjectFromSpecification
} from "../dataImport.helpers";
import {
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
import {
  buildImportDifferenceDecisionKey,
  buildImportRegionKey,
  getExcludedRowIndexesForRegion
} from "../dataImport.regions";

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
  pendingRegionKeys: ComputedRef<string[]>;
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
  const completedRequestAggregates = new Map<string, CombinedImportResult>();

  const buildExecutionRequestId = (checkpointKey: string) => {
    // 稳定且无需异步 WebCrypto：同一文件/范围在刷新页面后仍生成同一幂等键；
    // 服务端会再用完整 SHA-256 请求指纹校验，散列碰撞只会被拒绝，不会误复用结果。
    let first = 0x811c9dc5;
    let second = 0x9e3779b9;
    for (let index = 0; index < checkpointKey.length; index += 1) {
      const code = checkpointKey.charCodeAt(index);
      first = Math.imul(first ^ code, 0x01000193) >>> 0;
      second = Math.imul(second ^ code, 0x85ebca6b) >>> 0;
    }
    return `import_${first.toString(16).padStart(8, "0")}${second.toString(16).padStart(8, "0")}`;
  };

  const clearCompletedRequestCheckpoints = (fileId?: number) => {
    if (fileId == null) {
      completedRequestAggregates.clear();
      return;
    }
    const prefix = `${fileId}:`;
    for (const key of completedRequestAggregates.keys()) {
      if (key.startsWith(prefix)) completedRequestAggregates.delete(key);
    }
  };

  watch(
    () => options.uploadedFile.value?.fileId,
    (currentFileId, previousFileId) => {
      if (previousFileId !== currentFileId) {
        clearCompletedRequestCheckpoints(previousFileId);
      }
    }
  );

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

  const buildDifferenceKeysByRegion = (
    tableIndex: number,
    regionId?: string
  ) => {
    const confirmed: string[] = [];
    const partial: string[] = [];
    const skipped: string[] = [];
    for (const item of options.pendingDifferences.value) {
      if (
        item.tableIndex !== tableIndex ||
        (item.regionId ?? "default") !== (regionId ?? "default")
      )
        continue;
      const decision =
        options.differenceDecisionMap.value[
          buildImportDifferenceDecisionKey(item)
        ];
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
    includeDifferenceDecisions: boolean,
    allowedRegionKeys?: ReadonlySet<string>
  ): Promise<ImportBatchExecutionResult> => {
    const tableAggregates: CombinedImportResult[] = [];
    const duplicateCheckOptions = buildDuplicateCheckOptions();
    const plannedRegionKeys = configs.flatMap(cfg => {
      if (!options.isExcelFile.value) {
        const mappings = cfg.recognizedWordMappings?.length
          ? cfg.recognizedWordMappings
          : [];
        return (
          mappings.length
            ? mappings.map(mapping =>
                buildImportRegionKey(cfg.tableIndex, mapping.regionId)
              )
            : [buildImportRegionKey(cfg.tableIndex)]
        ).filter(key => !allowedRegionKeys || allowedRegionKeys.has(key));
      }
      const mappings = cfg.recognizedExcelMappings?.length
        ? cfg.recognizedExcelMappings
        : [cfg.excelMapping ?? defaultExcelMapping()];
      return mappings
        .map(mapping =>
          buildImportRegionKey(
            cfg.tableIndex,
            "regionId" in mapping && typeof mapping.regionId === "string"
              ? mapping.regionId
              : undefined
          )
        )
        .filter(key => !allowedRegionKeys || allowedRegionKeys.has(key));
    });
    const lastPlannedRegionKey = plannedRegionKeys.at(-1);

    for (const [idx, cfg] of configs.entries()) {
      options.importProgressText.value = buildImportProgressText(
        cfg,
        idx + 1,
        configs.length,
        includeDifferenceDecisions,
        duplicateCheckOptions
      );

      const fileId = options.uploadedFile.value?.fileId;
      const customerId = options.selectedCustomerId.value;
      if (fileId === undefined || customerId === undefined) {
        throw new Error("导入上下文已失效，请重新选择文件和目标客户");
      }

      const normalizedExcelMappings = options.isExcelFile.value
        ? (cfg.recognizedExcelMappings?.length
            ? cfg.recognizedExcelMappings
            : [cfg.excelMapping ?? defaultExcelMapping()]
          ).map((mapping, mappingIndex) => {
            const recognized = cfg.recognizedExcelMappings?.[mappingIndex];
            return {
              ...normalizeExcelMappingByTable(cfg.tableInfo, mapping),
              ...(recognized
                ? {
                    regionId: recognized.regionId,
                    regionIndex: recognized.regionIndex,
                    isSpecificationOnly: recognized.isSpecificationOnly
                  }
                : {})
            };
          })
        : [];
      if (
        options.isExcelFile.value &&
        normalizedExcelMappings.some(
          mapping =>
            (!("isSpecificationOnly" in mapping
              ? mapping.isSpecificationOnly
              : cfg.isSpecificationOnly) &&
              mapping.projectColumn === undefined) ||
            mapping.specificationColumn === undefined
        )
      ) {
        throw new Error(`工作表 ${cfg.tableIndex + 1} 缺少项目列或规格列映射`);
      }
      const shouldBackfillProject = shouldBackfillProjectFromSpecification(cfg);
      const buildCheckpointKey = (
        regionKey: string,
        mapping: unknown,
        excludedRowIndexes: readonly number[],
        decisions: { confirmed: string[]; partial: string[]; skipped: string[] }
      ) =>
        [
          fileId,
          customerId,
          options.selectedProcessId.value ?? "",
          options.selectedMachineModelId.value ?? "",
          regionKey,
          JSON.stringify(mapping),
          excludedRowIndexes.join(","),
          JSON.stringify(duplicateCheckOptions),
          options.previewSkippedRows.value ? "with-skipped-detail" : "summary",
          includeDifferenceDecisions ? "confirmation" : "initial",
          JSON.stringify(decisions)
        ].join(":");
      const appendResponse = (
        response: Awaited<ReturnType<typeof importExcelData>>,
        regionId: string | undefined,
        checkpointKey: string
      ) => {
        if (response.code !== 0) {
          // 任一分区失败后必须立即中止。继续请求后续分区会造成部分提交，且最后
          // 一个分区还可能清理源文件，使用户无法按已完成 checkpoint 安全重试。
          throw new Error(response.message || "导入失败，请稍后重试");
        }
        const aggregate: CombinedImportResult = createSingleTableAggregate(
          cfg.tableIndex,
          response.data,
          regionId
        );
        tableAggregates.push(aggregate);
        if (response.data.failedCount > 0) {
          const firstError = response.data.errors?.[0]?.message;
          throw new Error(
            firstError
              ? `当前区域有 ${response.data.failedCount} 条导入失败：${firstError}`
              : `当前区域有 ${response.data.failedCount} 条导入失败，请修正后重试`
          );
        }
        if (
          !response.data.requiresConfirmation ||
          (response.data.pendingCount || 0) === 0
        ) {
          completedRequestAggregates.set(checkpointKey, aggregate);
          if (completedRequestAggregates.size > 500) {
            const oldestKey = completedRequestAggregates.keys().next().value;
            if (oldestKey) completedRequestAggregates.delete(oldestKey);
          }
        }
      };

      if (options.isExcelFile.value) {
        for (const [
          regionIndex,
          mapping
        ] of normalizedExcelMappings.entries()) {
          const regionId = "regionId" in mapping ? mapping.regionId : undefined;
          const regionKey = buildImportRegionKey(cfg.tableIndex, regionId);
          if (allowedRegionKeys && !allowedRegionKeys.has(regionKey)) {
            continue;
          }
          const { confirmed, partial, skipped } = includeDifferenceDecisions
            ? buildDifferenceKeysByRegion(cfg.tableIndex, regionId)
            : { confirmed: [], partial: [], skipped: [] };
          const excludedRowIndexes = getExcludedRowIndexesForRegion(
            options.getExcludedRowIndexes(cfg.tableIndex),
            cfg.excelPreviewRowLocations ?? [],
            ("regionIndex" in mapping ? mapping.regionIndex : undefined) ??
              regionIndex,
            regionId
          );
          const excelImportMapping: CompleteExcelImportMapping = {
            ...mapping,
            projectColumn: mapping.projectColumn,
            specificationColumn: mapping.specificationColumn ?? 0
          };
          const checkpointKey = buildCheckpointKey(
            regionKey,
            excelImportMapping,
            excludedRowIndexes,
            { confirmed, partial, skipped }
          );
          const completedAggregate =
            completedRequestAggregates.get(checkpointKey);
          if (completedAggregate) {
            tableAggregates.push(completedAggregate);
            continue;
          }
          const response = await importExcelData({
            executionRequestId: buildExecutionRequestId(checkpointKey),
            ...excelImportMapping,
            fileId,
            sheetIndex: cfg.tableIndex,
            customerId,
            processId: options.selectedProcessId.value || undefined,
            machineModelId: options.selectedMachineModelId.value || undefined,
            cleanupSourceFile: regionKey === lastPlannedRegionKey,
            previewSkippedRows: options.previewSkippedRows.value,
            confirmedDifferenceKeys: confirmed,
            partiallyConfirmedDifferenceKeys: partial,
            skippedDifferenceKeys: skipped,
            excludedRowIndexes,
            duplicateCheckOptions,
            isSpecificationOnly:
              "isSpecificationOnly" in mapping
                ? mapping.isSpecificationOnly
                : shouldBackfillProject
          });
          appendResponse(response, regionId, checkpointKey);
        }
      } else {
        const wordMappings = cfg.recognizedWordMappings?.length
          ? cfg.recognizedWordMappings
          : [
              {
                ...cfg.wordMapping!,
                regionId: undefined,
                regionIndex: 0,
                headerRowCount: 1,
                dataEndRowIndex: undefined,
                isSpecificationOnly: shouldBackfillProject
              }
            ];
        for (const [regionIndex, mapping] of wordMappings.entries()) {
          const regionId = mapping.regionId;
          const regionKey = buildImportRegionKey(cfg.tableIndex, regionId);
          if (allowedRegionKeys && !allowedRegionKeys.has(regionKey)) continue;
          const { confirmed, partial, skipped } = includeDifferenceDecisions
            ? buildDifferenceKeysByRegion(cfg.tableIndex, regionId)
            : { confirmed: [], partial: [], skipped: [] };
          const excludedRowIndexes = getExcludedRowIndexesForRegion(
            options.getExcludedRowIndexes(cfg.tableIndex),
            cfg.excelPreviewRowLocations ?? [],
            mapping.regionIndex ?? regionIndex,
            regionId
          );
          const checkpointKey = buildCheckpointKey(
            regionKey,
            mapping,
            excludedRowIndexes,
            { confirmed, partial, skipped }
          );
          const completedAggregate =
            completedRequestAggregates.get(checkpointKey);
          if (completedAggregate) {
            tableAggregates.push(completedAggregate);
            continue;
          }
          const response = await importData({
            executionRequestId: buildExecutionRequestId(checkpointKey),
            fileId,
            tableIndex: cfg.tableIndex,
            customerId,
            processId: options.selectedProcessId.value || undefined,
            machineModelId: options.selectedMachineModelId.value || undefined,
            cleanupSourceFile: regionKey === lastPlannedRegionKey,
            previewSkippedRows: options.previewSkippedRows.value,
            confirmedDifferenceKeys: confirmed,
            partiallyConfirmedDifferenceKeys: partial,
            skippedDifferenceKeys: skipped,
            excludedRowIndexes,
            duplicateCheckOptions,
            regionId,
            headerRowCount: mapping.headerRowCount,
            dataEndRowIndex: mapping.dataEndRowIndex,
            isSpecificationOnly: mapping.isSpecificationOnly,
            mapping: {
              projectColumn: mapping.projectColumn,
              specificationColumn: mapping.specificationColumn,
              acceptanceColumn: mapping.acceptanceColumn,
              remarkColumn: mapping.remarkColumn,
              headerRowIndex: mapping.headerRowIndex,
              dataStartRowIndex: mapping.dataStartRowIndex
            }
          });
          appendResponse(response, regionId, checkpointKey);
        }
      }
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
      const pendingRegionSet = new Set(options.pendingRegionKeys.value);
      const pendingConfigs = options.tableConfigs.value.filter(cfg =>
        pendingSet.has(cfg.tableIndex)
      );
      options.importProgressText.value = `正在按确认结果继续导入 ${pendingConfigs.length} 个${options.isExcelFile.value ? "工作表" : "表格"}`;

      const batch = await executeImportBatch(
        pendingConfigs,
        true,
        pendingRegionSet
      );
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
      clearCompletedRequestCheckpoints(options.uploadedFile.value?.fileId);
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
      clearCompletedRequestCheckpoints(options.uploadedFile.value?.fileId);
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
