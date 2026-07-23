import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";

export type SmartFillConfirmSelectionFailure =
  | "no-selected-tables"
  | "table-not-found"
  | "missing-draft"
  | "confirm-failed";

export type SmartFillConfirmSelectionProgress = {
  completed: number;
  total: number;
  currentTableIndex?: number;
};

export type SmartFillConfirmSelectionResult =
  SmartFillConfirmSelectionProgress & {
    success: boolean;
    confirmedTableIndexes: number[];
    skippedTableIndexes: number[];
    failedTableIndex?: number;
    failure?: SmartFillConfirmSelectionFailure;
  };

type ConfirmSelectionOptions = {
  tables: readonly SmartConfigRecognizedTable[];
  selectedTableIndexes: readonly number[];
  draftRequests: ReadonlyMap<number, SmartConfigConfirmRequest>;
  confirm: (
    table: SmartConfigRecognizedTable,
    request: SmartConfigConfirmRequest
  ) => Promise<boolean>;
  onProgress?: (progress: SmartFillConfirmSelectionProgress) => void;
};

export const runSmartFillConfirmSelection = async ({
  tables,
  selectedTableIndexes,
  draftRequests,
  confirm,
  onProgress
}: ConfirmSelectionOptions): Promise<SmartFillConfirmSelectionResult> => {
  const selectedIndexes = [...new Set(selectedTableIndexes)].sort(
    (left, right) => left - right
  );
  const tableByIndex = new Map(tables.map(table => [table.tableIndex, table]));
  const missingTableIndex = selectedIndexes.find(
    tableIndex => !tableByIndex.has(tableIndex)
  );
  const selectedTables = selectedIndexes
    .map(tableIndex => tableByIndex.get(tableIndex))
    .filter((table): table is SmartConfigRecognizedTable => table != null);
  const pendingTables = selectedTables.filter(table => {
    const request = draftRequests.get(table.tableIndex);
    return table.decision !== "AutoApply" || request?.userModifiedStructure;
  });
  const pendingIndexes = new Set(pendingTables.map(table => table.tableIndex));
  const skippedTableIndexes = selectedTables
    .filter(table => !pendingIndexes.has(table.tableIndex))
    .map(table => table.tableIndex);
  const confirmedTableIndexes: number[] = [];
  const total = pendingTables.length;

  const fail = (
    failure: SmartFillConfirmSelectionFailure,
    failedTableIndex?: number
  ): SmartFillConfirmSelectionResult => ({
    success: false,
    completed: confirmedTableIndexes.length,
    total,
    confirmedTableIndexes: [...confirmedTableIndexes],
    skippedTableIndexes,
    failedTableIndex,
    failure
  });

  if (selectedIndexes.length === 0) return fail("no-selected-tables");
  if (missingTableIndex != null) {
    return fail("table-not-found", missingTableIndex);
  }

  const missingDraftTable = pendingTables.find(
    table => !draftRequests.has(table.tableIndex)
  );
  if (missingDraftTable) {
    return fail("missing-draft", missingDraftTable.tableIndex);
  }

  for (const table of pendingTables) {
    onProgress?.({
      completed: confirmedTableIndexes.length,
      total,
      currentTableIndex: table.tableIndex
    });
    try {
      const request = draftRequests.get(
        table.tableIndex
      ) as SmartConfigConfirmRequest;
      if (!(await confirm(table, request))) {
        return fail("confirm-failed", table.tableIndex);
      }
      confirmedTableIndexes.push(table.tableIndex);
    } catch {
      return fail("confirm-failed", table.tableIndex);
    }
  }

  onProgress?.({ completed: confirmedTableIndexes.length, total });
  return {
    success: true,
    completed: confirmedTableIndexes.length,
    total,
    confirmedTableIndexes,
    skippedTableIndexes
  };
};
