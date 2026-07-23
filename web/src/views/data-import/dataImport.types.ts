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

export type ExcelRegionMapping = ExcelSheetMapping & {
  regionId: string;
  regionIndex: number;
  isSpecificationOnly: boolean;
};

export type WordRegionMapping = ColumnMappingType & {
  regionId: string;
  regionIndex: number;
  headerRowCount: number;
  dataEndRowIndex?: number;
  isSpecificationOnly: boolean;
};

export type RegionPreviewRowLocation = {
  regionId: string;
  regionIndex: number;
  relativeRowIndex: number;
  displayRowNumber: number;
  headers: string[];
  mapping: ExcelRegionMapping | WordRegionMapping;
};

export type ExcelPreviewRowLocation = RegionPreviewRowLocation;

export type TableImportConfig = {
  tableIndex: number;
  tableInfo?: TableInfo;
  wordMapping?: ColumnMappingType;
  recognizedWordMappings?: WordRegionMapping[];
  excelMapping?: ExcelSheetMapping;
  recognizedExcelMapping?: ExcelSheetMapping;
  recognizedExcelMappings?: ExcelRegionMapping[];
  excelPreviewRowLocations?: ExcelPreviewRowLocation[];
  isSpecificationOnly?: boolean;
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

export type ImportErrorWithTable = {
  tableIndex: number;
  regionId?: string;
} & ImportResult["errors"][number];
export type ImportSkippedRowWithTable = {
  tableIndex: number;
  regionId?: string;
} & NonNullable<ImportResult["skippedRows"]>[number];
export type ImportPendingDifferenceWithTable = {
  tableIndex: number;
  regionId?: string;
} & NonNullable<ImportResult["pendingDifferences"]>[number];

export type DifferenceDecision = "import" | "partial" | "skip";

export type CombinedImportResult = Omit<
  ImportResult,
  "errors" | "skippedRows" | "pendingDifferences"
> & {
  tableIndex?: number;
  regionId?: string;
  errors: ImportErrorWithTable[];
  skippedRows: ImportSkippedRowWithTable[];
  pendingDifferences: ImportPendingDifferenceWithTable[];
};

export type ImportPreviewRow = {
  key: string;
  tableIndex: number;
  regionId?: string;
  regionIndex?: number;
  relativeRowIndex?: number;
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

export type SkippedPreviewColumn = { indexes: number[]; label: string };

export type SkippedRowsGroup = {
  tableIndex: number;
  regionId?: string;
  regionIndex?: number;
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
  getExisting: (
    item: ImportPendingDifferenceWithTable
  ) => string | null | undefined;
  getIncoming: (
    item: ImportPendingDifferenceWithTable
  ) => string | null | undefined;
};
