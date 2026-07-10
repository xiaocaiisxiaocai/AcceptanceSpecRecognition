import { ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";

const apiMocks = vi.hoisted(() => ({
  recognizeSmartConfig: vi.fn(),
  confirmSmartConfig: vi.fn(),
  getFileTables: vi.fn()
}));

vi.mock("@/api/smart-config", () => ({
  recognizeSmartConfig: apiMocks.recognizeSmartConfig,
  confirmSmartConfig: apiMocks.confirmSmartConfig
}));
vi.mock("@/api/document", () => ({ getFileTables: apiMocks.getFileTables }));
vi.mock("element-plus", () => ({
  ElMessage: {
    error: vi.fn(),
    warning: vi.fn(),
    success: vi.fn()
  }
}));

import { useDataImportSmartStructureRecognition } from "./useDataImportSmartStructureRecognition";

const oldTable: SmartConfigRecognizedTable = {
  tableIndex: 0,
  tableName: "Sheet1",
  headers: ["项目", "规格", "新项目", "验收", "备注"],
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  dataEndRowIndex: 8,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 3,
  remarkColumnIndex: 4,
  isSpecificationOnly: false,
  confidence: 0.95,
  source: "Rule",
  decision: "AutoApply",
  fields: []
};

describe("useDataImportSmartStructureRecognition", () => {
  beforeEach(() => {
    apiMocks.recognizeSmartConfig.mockReset().mockResolvedValue({
      code: 0,
      data: { fileId: 7, tables: [oldTable] }
    });
    apiMocks.confirmSmartConfig.mockReset().mockResolvedValue({
      code: 0,
      data: {
        templateSaved: true,
        templateId: 1,
        learnedRuleCount: 0,
        promotedGlobalRuleCount: 0,
        learningSucceeded: true
      }
    });
    apiMocks.getFileTables.mockReset().mockResolvedValue({
      code: 0,
      data: [
        {
          index: 0,
          name: "Sheet1",
          rowCount: 10,
          columnCount: 5,
          isNested: false,
          headers: oldTable.headers,
          hasMergedCells: false
        }
      ]
    });
  });

  it("确认新映射后再次切换 Sheet 仍保留已确认结构", async () => {
    const tableConfigs = ref<any[]>([]);
    const selectedSmartTableIndexes = ref<number[]>([]);
    const state = useDataImportSmartStructureRecognition({
      uploadedFile: ref({
        fileId: 7,
        fileName: "test.xlsx",
        fileType: 1,
        fileHash: "hash",
        isDuplicate: false,
        tableCount: 1,
        tableCountReady: true
      }),
      selectedCustomerId: ref(1),
      isExcelFile: ref(false),
      currentStep: ref(1),
      tableConfigs,
      selectedTableIndexes: ref<number[]>([]),
      selectedTables: ref<any[]>([]),
      activeTableIndex: ref<number | null>(null),
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes,
      ensurePreviewDataLoaded: vi.fn().mockResolvedValue(true)
    });

    await state.runSmartStructureRecognition();
    const request: SmartConfigConfirmRequest = {
      customerId: 1,
      fileId: 7,
      tableIndex: 0,
      templateName: "已确认",
      headers: oldTable.headers,
      projectColumnIndex: 2,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 3,
      remarkColumnIndex: 4,
      headerRowIndex: 2,
      headerRowCount: 1,
      dataStartRowIndex: 3,
      dataEndRowIndex: 8,
      isSpecificationOnly: false,
      learnedColumns: []
    };

    await state.handleSmartStructureConfirm(oldTable, request);
    await state.handleSmartTableImportSelectionChange(oldTable, true);

    expect(tableConfigs.value[0]?.wordMapping).toMatchObject({
      projectColumn: 2,
      specificationColumn: 1,
      headerRowIndex: 2,
      dataStartRowIndex: 3
    });
  });
});
