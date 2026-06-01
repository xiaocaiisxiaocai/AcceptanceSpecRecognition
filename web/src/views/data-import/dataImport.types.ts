import type {
  ColumnMapping as ColumnMappingType,
  ImportDuplicateCheckOptions,
  ImportResult,
  TableData,
  TableInfo
} from "@/api/document";

export type ExcelSheetMapping = {
  projectColumn?: number;
  specificationColumn?: number;
  acceptanceColumn?: number;
  remarkColumn?: number;
  headerRowStart: number;
  headerRowCount: number;
  dataStartRow: number;
  dataEndRow: number;
};

export type TableImportConfig = {
  tableIndex: number;
  tableInfo?: TableInfo;
  wordMapping?: ColumnMappingType;
  excelMapping?: ExcelSheetMapping;
  previewData: TableData | null;
};

export type MappingClipboard =
  | { kind: "excel"; value: ExcelSheetMapping }
  | { kind: "word"; value: ColumnMappingType };

export type ImportDuplicateAiConfig = Required<
  Pick<
    ImportDuplicateCheckOptions,
    | "enableSemanticDuplicateCheck"
    | "semanticTopK"
    | "semanticMinScore"
    | "enableLlmDuplicateReview"
    | "llmPassScore"
    | "highConfidenceThreshold"
  >
> & {
  embeddingServiceId?: number;
  llmServiceId?: number;
};

export type ImportErrorWithTable = { tableIndex: number } & ImportResult["errors"][number];
export type ImportSkippedRowWithTable = {
  tableIndex: number;
} & NonNullable<ImportResult["skippedRows"]>[number];
export type ImportPendingDifferenceWithTable = {
  tableIndex: number;
} & NonNullable<ImportResult["pendingDifferences"]>[number];

export type DifferenceDecision = "import" | "partial" | "skip";

export type CombinedImportResult = Omit<
  ImportResult,
  "errors" | "skippedRows" | "pendingDifferences"
> & {
  errors: ImportErrorWithTable[];
  skippedRows: ImportSkippedRowWithTable[];
  pendingDifferences: ImportPendingDifferenceWithTable[];
};

export type ImportPreviewRow = {
  key: string;
  tableIndex: number;
  rowIndex: number;
  displayRowNumber: number;
  project: string;
  specification: string;
  acceptance: string;
  remark: string;
};

export type ImportPreviewGroup = {
  tableIndex: number;
  label: string;
  rows: ImportPreviewRow[];
};

export type SkippedPreviewColumn = { index: number; label: string };

export type SkippedRowsGroup = {
  tableIndex: number;
  rows: ImportSkippedRowWithTable[];
  columns: SkippedPreviewColumn[];
};

export type ImportBatchExecutionResult = {
  aggregate: CombinedImportResult;
  tableAggregates: CombinedImportResult[];
};

export type DifferenceColumnDef = {
  key: "project" | "specification" | "acceptance" | "remark";
  label: string;
  getExisting: (item: ImportPendingDifferenceWithTable) => string | null | undefined;
  getIncoming: (item: ImportPendingDifferenceWithTable) => string | null | undefined;
};
