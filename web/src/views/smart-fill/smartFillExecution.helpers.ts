import type {
  BatchExecuteFillRequest,
  BatchTablePreviewResult,
  BatchTableRegionConfig,
  MatchConfig,
  MatchPreviewItem,
  MatchResult
} from "@/api/matching";

export type SmartFillScope = {
  customerId?: number;
  processId?: number;
  machineModelId?: number;
};

export type SmartFillSelection = {
  rowIndex: number;
  specId?: number;
  manualConfirmed?: boolean;
  manualFill?: boolean;
  reviewApprovalToken?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
};

export type SmartFillExecuteTableConfig = {
  tableIndex: number;
  acceptanceColumnIndex: number;
  remarkColumnIndex?: number;
  projectColumnIndex: number;
  specificationColumnIndex: number;
  headerRowStart?: number;
  headerRowCount?: number;
  dataStartRow?: number;
  dataEndRow?: number;
  regions?: BatchTableRegionConfig[];
  filterEmptySourceRows?: boolean;
};

export type SmartFillBackfilledItem = {
  tableIndex: number;
  rowIndex: number;
  specId?: number;
  sourceProject?: string;
  sourceSpecification?: string;
  overrideAcceptance?: string;
  overrideRemark?: string;
  actionType: "update" | "create";
};

export const applyBackfilledItemsToPreviewResults = (
  results: BatchTablePreviewResult[],
  backfilledItems: SmartFillBackfilledItem[]
): BatchTablePreviewResult[] => {
  const updatedBySpecId = new Map(
    backfilledItems
      .filter(
        (
          item
        ): item is SmartFillBackfilledItem & {
          specId: number;
          actionType: "update";
        } => item.actionType === "update" && !!item.specId
      )
      .map(item => [item.specId, item])
  );

  if (updatedBySpecId.size === 0) {
    return results;
  }

  return results.map(table => ({
    ...table,
    items: table.items.map(item => {
      const updatedItem = item.bestMatch?.specId
        ? updatedBySpecId.get(item.bestMatch.specId)
        : undefined;
      if (!item.bestMatch || !updatedItem) {
        return item;
      }

      return {
        ...item,
        bestMatch: {
          ...item.bestMatch,
          project: updatedItem.sourceProject ?? item.bestMatch.project,
          specification:
            updatedItem.sourceSpecification ?? item.bestMatch.specification,
          acceptance:
            updatedItem.overrideAcceptance ?? item.bestMatch.acceptance,
          remark: updatedItem.overrideRemark ?? item.bestMatch.remark,
          reviewApprovalToken: undefined
        }
      };
    })
  }));
};

const cloneMatchResultForExecutionHistory = (
  bestMatch?: MatchResult
): MatchResult | undefined => {
  if (!bestMatch) return undefined;

  return {
    ...bestMatch,
    scoreDetails: { ...(bestMatch.scoreDetails ?? {}) },
    evidenceSummary: [...(bestMatch.evidenceSummary ?? [])],
    conflictSummary: [...(bestMatch.conflictSummary ?? [])],
    issues: [...(bestMatch.issues ?? [])],
    entities: [...(bestMatch.entities ?? [])],
    llmEquivalence: bestMatch.llmEquivalence
      ? { ...bestMatch.llmEquivalence }
      : undefined,
    topCandidates: (bestMatch.topCandidates ?? []).map(candidate => ({
      ...candidate,
      scoreDetails: { ...(candidate.scoreDetails ?? {}) },
      evidenceSummary: [...(candidate.evidenceSummary ?? [])],
      conflictSummary: [...(candidate.conflictSummary ?? [])],
      issues: [...(candidate.issues ?? [])],
      entities: [...(candidate.entities ?? [])],
      llmEquivalence: candidate.llmEquivalence
        ? { ...candidate.llmEquivalence }
        : undefined
    }))
  };
};

const getBackfilledItemKey = (tableIndex: number, rowIndex: number) =>
  `${tableIndex}:${rowIndex}`;

export const buildExecutionHistoryPreviewTables = (
  previewResults: BatchTablePreviewResult[],
  tableIndexes: number[]
) => {
  const selectedTableIndexes = new Set(tableIndexes);

  return previewResults
    .filter(result => selectedTableIndexes.has(result.tableIndex))
    .map(result => ({
      tableIndex: result.tableIndex,
      items: result.items.map((item: MatchPreviewItem) => ({
        regionId: item.regionId,
        regionIndex: item.regionIndex,
        acceptanceColumnIndex: item.acceptanceColumnIndex,
        remarkColumnIndex: item.remarkColumnIndex,
        rowIndex: item.rowIndex,
        sourceProject: item.sourceProject,
        sourceSpecification: item.sourceSpecification,
        bestMatch: cloneMatchResultForExecutionHistory(item.bestMatch),
        llmReviewDraft: item.llmReviewDraft,
        llmReviewError: item.llmReviewError,
        llmReviewStage: item.llmReviewStage,
        noMatchReason: item.noMatchReason,
        hasMatch: item.hasMatch,
        confidenceLevel: item.confidenceLevel
      }))
    }));
};

export const buildSmartFillExecuteRequest = ({
  uploadedFileId,
  scope,
  selectedConfigs,
  allSelections,
  matchConfig,
  highConfidenceThreshold,
  previewResults
}: {
  uploadedFileId?: number;
  scope: SmartFillScope;
  selectedConfigs: SmartFillExecuteTableConfig[];
  allSelections: Map<number, SmartFillSelection[]>;
  matchConfig: MatchConfig;
  highConfidenceThreshold: number;
  previewResults: BatchTablePreviewResult[];
}): BatchExecuteFillRequest | null => {
  const tables = selectedConfigs
    .map(config => {
      const selections = allSelections.get(config.tableIndex) || [];
      if (selections.length === 0) return null;

      return {
        tableIndex: config.tableIndex,
        acceptanceColumnIndex: config.acceptanceColumnIndex,
        remarkColumnIndex: config.remarkColumnIndex,
        projectColumnIndex: config.projectColumnIndex,
        specificationColumnIndex: config.specificationColumnIndex,
        headerRowStart: config.headerRowStart,
        headerRowCount: config.headerRowCount,
        dataStartRow: config.dataStartRow,
        dataEndRow: config.dataEndRow,
        regions: config.regions,
        filterEmptySourceRows: true,
        mappings: selections.map(s => ({
          rowIndex: s.rowIndex,
          specId: s.specId,
          manualConfirmed: s.manualConfirmed,
          manualFill: s.manualFill,
          reviewApprovalToken: s.reviewApprovalToken,
          overrideAcceptance: s.overrideAcceptance,
          overrideRemark: s.overrideRemark
        }))
      };
    })
    .filter(Boolean) as BatchExecuteFillRequest["tables"];

  if (tables.length === 0 || uploadedFileId === undefined) {
    return null;
  }

  return {
    fileId: uploadedFileId,
    customerId: scope.customerId,
    processId: scope.processId,
    machineModelId: scope.machineModelId,
    config: {
      ...matchConfig,
      highConfidenceThreshold,
      filterEmptySourceRows: true
    },
    previewTables: buildExecutionHistoryPreviewTables(
      previewResults,
      tables.map(table => table.tableIndex)
    ),
    tables
  };
};

export const refreshBackfilledExecuteRequest = (
  request: BatchExecuteFillRequest,
  backfilledItems: SmartFillBackfilledItem[]
): BatchExecuteFillRequest => {
  const updatedItemLookup = new Map(
    backfilledItems
      .filter(item => item.actionType === "update" && item.specId)
      .map(item => [getBackfilledItemKey(item.tableIndex, item.rowIndex), item])
  );

  if (updatedItemLookup.size === 0) {
    return request;
  }

  const tables = request.tables.map(table => ({
    ...table,
    mappings: table.mappings.map(mapping => {
      const updatedItem = updatedItemLookup.get(
        getBackfilledItemKey(table.tableIndex, mapping.rowIndex)
      );
      if (!updatedItem || updatedItem.specId !== mapping.specId) {
        return { ...mapping };
      }

      // 回填已更新主数据，旧 token 的规格指纹会失效；改为人工确认重新走当前匹配门禁。
      return {
        ...mapping,
        manualConfirmed: true,
        reviewApprovalToken: undefined,
        overrideAcceptance:
          updatedItem.overrideAcceptance ?? mapping.overrideAcceptance,
        overrideRemark: updatedItem.overrideRemark ?? mapping.overrideRemark
      };
    })
  }));

  const previewTables = request.previewTables?.map(table => ({
    ...table,
    items: table.items.map(item => {
      const updatedItem = updatedItemLookup.get(
        getBackfilledItemKey(table.tableIndex, item.rowIndex)
      );
      const clonedBestMatch = cloneMatchResultForExecutionHistory(
        item.bestMatch
      );
      if (!updatedItem || updatedItem.specId !== item.bestMatch?.specId) {
        return {
          ...item,
          bestMatch: clonedBestMatch
        };
      }

      return {
        ...item,
        bestMatch: clonedBestMatch
          ? {
              ...clonedBestMatch,
              acceptance:
                updatedItem.overrideAcceptance ?? clonedBestMatch.acceptance,
              remark: updatedItem.overrideRemark ?? clonedBestMatch.remark,
              reviewApprovalToken: undefined
            }
          : undefined
      };
    })
  }));

  return {
    ...request,
    previewTables,
    tables
  };
};
