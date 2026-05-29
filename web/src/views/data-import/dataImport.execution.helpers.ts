import type { ImportResult } from "@/api/document";
import type {
  CombinedImportResult
} from "./dataImport.types";

export const buildEmptyImportAggregate = (): CombinedImportResult => ({
  successCount: 0,
  failedCount: 0,
  skippedCount: 0,
  totalCount: 0,
  errors: [],
  skippedRows: [],
  requiresConfirmation: false,
  pendingCount: 0,
  pendingDifferences: []
});

export const mergeImportAggregates = (
  ...aggregates: Array<CombinedImportResult | null | undefined>
): CombinedImportResult => {
  const merged = buildEmptyImportAggregate();

  for (const aggregate of aggregates) {
    if (!aggregate) continue;

    merged.successCount += aggregate.successCount;
    merged.failedCount += aggregate.failedCount;
    merged.skippedCount += aggregate.skippedCount;
    merged.totalCount += aggregate.totalCount;
    merged.requiresConfirmation =
      merged.requiresConfirmation || !!aggregate.requiresConfirmation;
    merged.pendingCount += aggregate.pendingCount || 0;
    merged.errors.push(...(aggregate.errors || []));
    merged.skippedRows.push(...(aggregate.skippedRows || []));
    merged.pendingDifferences.push(...(aggregate.pendingDifferences || []));
  }

  return merged;
};

export const createSingleTableAggregate = (
  tableIndex: number,
  result: ImportResult
): CombinedImportResult => ({
  successCount: result.successCount,
  failedCount: result.failedCount,
  skippedCount: result.skippedCount,
  totalCount: result.totalCount,
  errors: (result.errors || []).map(error => ({
    tableIndex,
    ...error
  })),
  skippedRows: (result.skippedRows || []).map(row => ({
    tableIndex,
    ...row
  })),
  requiresConfirmation: !!result.requiresConfirmation,
  pendingCount: result.pendingCount || 0,
  pendingDifferences: (result.pendingDifferences || []).map(item => ({
    tableIndex,
    ...item
  }))
});

export const splitBatchAggregates = (tableAggregates: CombinedImportResult[]) => {
  const pending: CombinedImportResult[] = [];
  const completed: CombinedImportResult[] = [];

  for (const aggregate of tableAggregates) {
    if ((aggregate.pendingCount || 0) > 0 || aggregate.pendingDifferences.length > 0) {
      pending.push(aggregate);
      continue;
    }

    completed.push(aggregate);
  }

  return {
    pending: mergeImportAggregates(...pending),
    completed: mergeImportAggregates(...completed)
  };
};
