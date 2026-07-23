export type SmartStructureBatchTable = {
  tableIndex: number;
  decision: string;
};

export type SmartStructureBatchConfirmPhase =
  | "validating"
  | "confirming"
  | "refreshing"
  | "importing"
  | "completed"
  | "failed";

export type SmartStructureBatchConfirmProgress = {
  phase: SmartStructureBatchConfirmPhase;
  completed: number;
  total: number;
  currentTableIndex?: number;
};

export type SmartStructureBatchConfirmFailure =
  | "no-selected-tables"
  | "table-not-found"
  | "missing-draft"
  | "confirm-failed"
  | "refresh-failed"
  | "import-failed";

export type SmartStructureBatchConfirmResult =
  SmartStructureBatchConfirmProgress & {
    success: boolean;
    confirmedTableIndexes: number[];
    skippedTableIndexes: number[];
    failedTableIndex?: number;
    failure?: SmartStructureBatchConfirmFailure;
    error?: unknown;
  };

type SmartStructureBatchConfirmOptions<
  TRequest,
  TTable extends SmartStructureBatchTable
> = {
  tables: readonly TTable[];
  selectedTableIndexes: readonly number[];
  draftRequests: ReadonlyMap<number, TRequest>;
  requiresConfirmation?: (table: TTable, request?: TRequest) => boolean;
  confirm: (table: TTable, request: TRequest) => Promise<boolean>;
  refresh: () => Promise<boolean | void>;
  importData: () => Promise<void>;
  onProgress?: (progress: SmartStructureBatchConfirmProgress) => void;
};

const emitProgress = (
  onProgress:
    | ((progress: SmartStructureBatchConfirmProgress) => void)
    | undefined,
  progress: SmartStructureBatchConfirmProgress
) => {
  onProgress?.(progress);
};

/**
 * Confirms every selected pending Sheet in worksheet order, then refreshes and
 * imports exactly once. Validation happens before the first confirmation so a
 * missing draft cannot leave the batch partially learned.
 */
export const runSmartStructureBatchConfirmImportAction = async <
  TRequest,
  TTable extends SmartStructureBatchTable
>({
  tables,
  selectedTableIndexes,
  draftRequests,
  requiresConfirmation = table => table.decision !== "AutoApply",
  confirm,
  refresh,
  importData,
  onProgress
}: SmartStructureBatchConfirmOptions<
  TRequest,
  TTable
>): Promise<SmartStructureBatchConfirmResult> => {
  const selectedIndexes = [...new Set(selectedTableIndexes)].sort(
    (left, right) => left - right
  );
  const tableByIndex = new Map(tables.map(table => [table.tableIndex, table]));
  const missingTableIndex = selectedIndexes.find(
    tableIndex => !tableByIndex.has(tableIndex)
  );
  const selectedTables = selectedIndexes
    .map(tableIndex => tableByIndex.get(tableIndex))
    .filter((table): table is TTable => table != null);
  const pendingTables = selectedTables.filter(table =>
    requiresConfirmation(table, draftRequests.get(table.tableIndex))
  );
  const pendingTableIndexes = new Set(
    pendingTables.map(table => table.tableIndex)
  );
  const skippedTableIndexes = selectedTables
    .filter(table => !pendingTableIndexes.has(table.tableIndex))
    .map(table => table.tableIndex);
  const total = pendingTables.length;
  const confirmedTableIndexes: number[] = [];

  const fail = (
    failure: SmartStructureBatchConfirmFailure,
    failedTableIndex?: number,
    error?: unknown
  ): SmartStructureBatchConfirmResult => {
    const progress: SmartStructureBatchConfirmProgress = {
      phase: "failed",
      completed: confirmedTableIndexes.length,
      total,
      ...(failedTableIndex == null
        ? {}
        : { currentTableIndex: failedTableIndex })
    };
    emitProgress(onProgress, progress);
    return {
      ...progress,
      success: false,
      confirmedTableIndexes: [...confirmedTableIndexes],
      skippedTableIndexes,
      failedTableIndex,
      failure,
      error
    };
  };

  emitProgress(onProgress, { phase: "validating", completed: 0, total });

  if (selectedIndexes.length === 0) {
    return fail("no-selected-tables");
  }
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
    emitProgress(onProgress, {
      phase: "confirming",
      completed: confirmedTableIndexes.length,
      total,
      currentTableIndex: table.tableIndex
    });

    try {
      const request = draftRequests.get(table.tableIndex) as TRequest;
      if (!(await confirm(table, request))) {
        return fail("confirm-failed", table.tableIndex);
      }
      confirmedTableIndexes.push(table.tableIndex);
    } catch (error) {
      return fail("confirm-failed", table.tableIndex, error);
    }
  }

  emitProgress(onProgress, {
    phase: "refreshing",
    completed: confirmedTableIndexes.length,
    total
  });
  try {
    if ((await refresh()) === false) {
      return fail("refresh-failed");
    }
  } catch (error) {
    return fail("refresh-failed", undefined, error);
  }

  emitProgress(onProgress, {
    phase: "importing",
    completed: confirmedTableIndexes.length,
    total
  });
  try {
    await importData();
  } catch (error) {
    return fail("import-failed", undefined, error);
  }

  const progress: SmartStructureBatchConfirmProgress = {
    phase: "completed",
    completed: confirmedTableIndexes.length,
    total
  };
  emitProgress(onProgress, progress);
  return {
    ...progress,
    success: true,
    confirmedTableIndexes,
    skippedTableIndexes
  };
};
