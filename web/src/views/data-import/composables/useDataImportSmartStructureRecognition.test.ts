import { ref } from "vue";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type {
  SmartConfigConfirmRequest,
  SmartConfigRecognizedTable
} from "@/api/smart-config";

const apiMocks = vi.hoisted(() => ({
  recognizeSmartConfig: vi.fn(),
  confirmSmartConfig: vi.fn(),
  getFileTables: vi.fn(),
  getAiServiceSelection: vi.fn()
}));

vi.mock("@/api/smart-config", () => ({
  recognizeSmartConfig: apiMocks.recognizeSmartConfig,
  confirmSmartConfig: apiMocks.confirmSmartConfig
}));
vi.mock("@/api/document", () => ({ getFileTables: apiMocks.getFileTables }));
vi.mock("@/api/ai-service", () => ({
  getAiServiceSelection: apiMocks.getAiServiceSelection
}));
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
  decision: "NeedConfirm",
  recommendation: "NeedConfirm",
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
    apiMocks.getAiServiceSelection.mockReset().mockResolvedValue({
      code: 0,
      data: { status: "available", serviceId: 3 }
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
    expect(apiMocks.recognizeSmartConfig).toHaveBeenCalledWith({
      fileId: 7,
      customerId: 1,
      enableLlmAssistance: false,
      llmServiceId: undefined
    });
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

    expect(await state.handleSmartStructureConfirm(oldTable, request)).toBe(
      true
    );
    await state.handleSmartTableImportSelectionChange(oldTable, true);

    expect(tableConfigs.value[0]?.wordMapping).toMatchObject({
      projectColumn: 2,
      specificationColumn: 1,
      headerRowIndex: 2,
      dataStartRowIndex: 3
    });
  });

  it("确认接口失败时返回失败且不重新应用导入配置", async () => {
    const ensurePreviewDataLoaded = vi.fn().mockResolvedValue(true);
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
      isExcelFile: ref(true),
      currentStep: ref(1),
      tableConfigs: ref<any[]>([]),
      selectedTableIndexes: ref<number[]>([]),
      selectedTables: ref<any[]>([]),
      activeTableIndex: ref<number | null>(null),
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes: ref<number[]>([0]),
      ensurePreviewDataLoaded
    });

    await state.runSmartStructureRecognition();
    apiMocks.confirmSmartConfig.mockResolvedValueOnce({
      code: 500,
      message: "保存失败"
    });

    const confirmed = await state.handleSmartStructureConfirm(oldTable, {
      customerId: 1,
      fileId: 7,
      tableIndex: 0,
      templateName: "Sheet1",
      headers: oldTable.headers,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 3,
      remarkColumnIndex: 4,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 8,
      isSpecificationOnly: false,
      learnedColumns: []
    });

    expect(confirmed).toBe(false);
    expect(ensurePreviewDataLoaded).toHaveBeenCalledOnce();
  });

  it("确认成功但导入配置刷新失败时返回失败", async () => {
    const ensurePreviewDataLoaded = vi
      .fn()
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false);
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
      isExcelFile: ref(true),
      currentStep: ref(1),
      tableConfigs: ref<any[]>([]),
      selectedTableIndexes: ref<number[]>([]),
      selectedTables: ref<any[]>([]),
      activeTableIndex: ref<number | null>(null),
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes: ref<number[]>([0]),
      ensurePreviewDataLoaded
    });

    await state.runSmartStructureRecognition();

    expect(
      await state.handleSmartStructureConfirm(oldTable, {
        customerId: 1,
        fileId: 7,
        tableIndex: 0,
        templateName: "Sheet1",
        headers: oldTable.headers,
        projectColumnIndex: 0,
        specificationColumnIndex: 1,
        acceptanceColumnIndex: 3,
        remarkColumnIndex: 4,
        headerRowIndex: 0,
        headerRowCount: 1,
        dataStartRowIndex: 1,
        dataEndRowIndex: 8,
        isSpecificationOnly: false,
        learnedColumns: []
      })
    ).toBe(false);
    expect(ensurePreviewDataLoaded).toHaveBeenCalledTimes(2);
  });

  it("手动勾选缺少必填列的待确认 Sheet 时保留勾选状态但不生成导入配置", async () => {
    const pendingTable: SmartConfigRecognizedTable = {
      ...oldTable,
      decision: "NeedConfirm",
      recommendation: "Optional",
      projectColumnIndex: undefined,
      acceptanceColumnIndex: undefined
    };
    apiMocks.recognizeSmartConfig.mockResolvedValue({
      code: 0,
      data: { fileId: 7, tables: [pendingTable] }
    });

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

    const recognized = await state.runSmartStructureRecognition();
    await state.handleSmartTableImportSelectionChange(pendingTable, true);

    expect(recognized).toBe(true);
    expect(state.smartTableInfos.value).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ index: 0, rowCount: 10, columnCount: 5 })
      ])
    );
    expect(selectedSmartTableIndexes.value).toEqual([0]);
    expect(tableConfigs.value).toEqual([]);
  });

  it("Reject Sheet 进入手动处理时会生成默认配置并定位到该 Sheet", async () => {
    const tableConfigs = ref<any[]>([]);
    const activeTableIndex = ref<number | null>(null);
    const selectedTableIndexes = ref<number[]>([]);
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
      isExcelFile: ref(true),
      currentStep: ref(1),
      tableConfigs,
      selectedTableIndexes,
      selectedTables: ref<any[]>([]),
      activeTableIndex,
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes: ref<number[]>([]),
      ensurePreviewDataLoaded: vi.fn().mockResolvedValue(true)
    });

    await state.runSmartStructureRecognition();
    tableConfigs.value = [];
    selectedTableIndexes.value = [];

    expect(state.prepareAdvancedTableConfig(0)).toBe(true);
    expect(activeTableIndex.value).toBe(0);
    expect(selectedTableIndexes.value).toEqual([0]);
    expect(tableConfigs.value[0]).toMatchObject({
      tableIndex: 0,
      previewData: null
    });
    expect(tableConfigs.value[0].excelMapping).toBeDefined();
  });

  it("识别成功但预览失败时暴露手动兜底错误状态", async () => {
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
      currentStep: ref(0),
      tableConfigs: ref<any[]>([]),
      selectedTableIndexes: ref<number[]>([]),
      selectedTables: ref<any[]>([]),
      activeTableIndex: ref<number | null>(null),
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes: ref<number[]>([]),
      ensurePreviewDataLoaded: vi.fn().mockResolvedValue(false)
    });

    expect(await state.runSmartStructureRecognition()).toBe(false);
    expect(state.smartApplyError.value).toContain("预览生成失败");
    expect(state.recognizedTables.value).toHaveLength(1);
  });

  it("识别到预览完成前重复触发时只执行一次完整流程", async () => {
    let resolvePreview!: (value: boolean) => void;
    const ensurePreviewDataLoaded = vi.fn(
      () =>
        new Promise<boolean>(resolve => {
          resolvePreview = resolve;
        })
    );
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
      isExcelFile: ref(true),
      currentStep: ref(0),
      tableConfigs: ref<any[]>([]),
      selectedTableIndexes: ref<number[]>([]),
      selectedTables: ref<any[]>([]),
      activeTableIndex: ref<number | null>(null),
      importPreviewSelectionKeys: ref<string[]>([]),
      excludedRowIndexMap: ref<Record<number, number[]>>({}),
      smartStageText: ref(""),
      selectedSmartTableIndexes: ref<number[]>([]),
      ensurePreviewDataLoaded
    });

    const firstRun = state.runSmartStructureRecognition();
    await vi.waitFor(() =>
      expect(ensurePreviewDataLoaded).toHaveBeenCalledOnce()
    );

    expect(state.smartRecognizing.value).toBe(true);
    expect(await state.runSmartStructureRecognition()).toBe(false);
    expect(apiMocks.recognizeSmartConfig).toHaveBeenCalledOnce();

    resolvePreview(true);
    expect(await firstRun).toBe(true);
    expect(state.smartRecognizing.value).toBe(false);
  });
});
