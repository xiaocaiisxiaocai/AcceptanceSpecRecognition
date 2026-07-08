import { describe, expect, it } from "vitest";
import {
  buildDataImportPreviewStageText,
  buildDataImportConfigsFromRecognizedTables,
  createDataImportSmartSteps,
  createDefaultSelectedSmartTableIndexes,
  filterSelectedSmartTables,
  getDataImportPreviewLoadState,
  getDataImportPreviewTotalCount,
  getDataImportPrevStepState,
  getDataImportAdvancedStep
} from "./dataImport.smartRecognition";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { TableInfo } from "@/api/document";

const tableInfo = (index: number): TableInfo => ({
  index,
  name: `Sheet${index + 1}`,
  rowCount: 12,
  columnCount: 4,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注"],
  hasMergedCells: false,
  usedRangeStartRow: 3,
  usedRangeStartColumn: 2
});

const recognizedTable = (
  overrides: Partial<SmartConfigRecognizedTable>
): SmartConfigRecognizedTable => ({
  tableIndex: 0,
  tableName: "Sheet1",
  headers: ["项目", "规格", "验收", "备注"],
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  dataEndRowIndex: 8,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  isSpecificationOnly: false,
  confidence: 0.98,
  source: "Rule",
  decision: "AutoApply",
  fields: [],
  ...overrides
});

describe("dataImport.smartRecognition", () => {
  it("生成智能导入阶段提示文案", () => {
    expect(buildDataImportPreviewStageText(2, 5, "Sheet2")).toBe(
      "正在生成导入预览：第 2/5 张（Sheet2）"
    );
    expect(buildDataImportPreviewStageText(1, 1)).toBe(
      "正在生成导入预览：第 1/1 张"
    );
  });

  it("创建三步式智能导入步骤", () => {
    expect(createDataImportSmartSteps()).toEqual([
      { title: "上传/目标", description: "上传文件并选择业务归属" },
      { title: "确认/预览", description: "确认识别结构并预览待导入数据" },
      { title: "完成", description: "执行导入并查看结果" }
    ]);
  });

  it("Word 识别结果转为导入配置时保留 0-based 映射", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0]).toMatchObject({
      tableIndex: 0,
      wordMapping: {
        projectColumn: 0,
        specificationColumn: 1,
        acceptanceColumn: 2,
        remarkColumn: 3,
        headerRowIndex: 0,
        dataStartRowIndex: 1
      },
      previewData: null
    });
  });

  it("Excel 识别结果转为导入配置时转换为实际 1-based 行列号", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });

    expect(configs[0].excelMapping).toEqual({
      projectColumn: 2,
      specificationColumn: 3,
      acceptanceColumn: 4,
      remarkColumn: 5,
      headerRowStart: 3,
      headerRowCount: 1,
      dataStartRow: 4,
      dataEndRow: 11
    });
    expect(configs[0].recognizedExcelMapping).toEqual(configs[0].excelMapping);
  });

  it("跳过 Reject 和缺少任一必填导入列的表", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [
        recognizedTable({ tableIndex: 0, decision: "Reject" }),
        recognizedTable({
          tableIndex: 1,
          decision: "NeedConfirm",
          specificationColumnIndex: undefined
        }),
        recognizedTable({
          tableIndex: 3,
          decision: "NeedConfirm",
          acceptanceColumnIndex: undefined
        }),
        recognizedTable({
          tableIndex: 4,
          decision: "NeedConfirm",
          remarkColumnIndex: undefined
        }),
        recognizedTable({ tableIndex: 2, decision: "NeedConfirm" })
      ],
      tableInfos: [
        tableInfo(0),
        tableInfo(1),
        tableInfo(2),
        tableInfo(3),
        tableInfo(4)
      ]
    });

    expect(configs.map(item => item.tableIndex)).toEqual([2]);
  });

  it("默认仅选中可导入的识别表", () => {
    expect(
      createDefaultSelectedSmartTableIndexes([
        recognizedTable({ tableIndex: 0, decision: "AutoApply" }),
        recognizedTable({
          tableIndex: 4,
          decision: "NeedConfirm",
          confidence: 0,
          fields: [
            {
              field: "Specification",
              columnIndex: 1,
              header: "规格",
              confidence: 0,
              source: "Rule"
            }
          ]
        }),
        recognizedTable({
          tableIndex: 1,
          decision: "NeedConfirm",
          specificationColumnIndex: undefined
        }),
        recognizedTable({
          tableIndex: 5,
          decision: "NeedConfirm",
          acceptanceColumnIndex: undefined
        }),
        recognizedTable({
          tableIndex: 6,
          decision: "NeedConfirm",
          remarkColumnIndex: undefined
        }),
        recognizedTable({ tableIndex: 2, decision: "Reject" }),
        recognizedTable({ tableIndex: 3, decision: "NeedConfirm" })
      ])
    ).toEqual([0, 3]);
  });

  it("按用户勾选过滤需要导入的识别表", () => {
    const tables = [
      recognizedTable({ tableIndex: 0 }),
      recognizedTable({ tableIndex: 1 }),
      recognizedTable({ tableIndex: 2 })
    ];

    expect(filterSelectedSmartTables(tables, [2, 0]).map(t => t.tableIndex)).toEqual([
      0,
      2
    ]);
  });

  it("高级步骤仅允许进入旧表格选择或映射步骤", () => {
    expect(getDataImportAdvancedStep("tableSelect")).toBe(1);
    expect(getDataImportAdvancedStep("mapping")).toBe(2);
  });

  it("高级模式从表格选择页返回时应回到智能上传页，避免出现高级模式第 0 步空白页", () => {
    expect(
      getDataImportPrevStepState({
        advancedMode: true,
        currentStep: 1
      })
    ).toEqual({
      advancedMode: false,
      currentStep: 0
    });
  });

  it("部分预览时预计导入数量应按总行数统计，并扣除已移除行", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });
    configs[0].previewData = {
      tableIndex: 0,
      headers: ["项目", "规格"],
      rows: [["A", "B"]],
      totalRows: 8,
      columnCount: 2
    };

    expect(getDataImportPreviewTotalCount(configs, { 0: [0, 3] })).toBe(6);
  });

  it("可判断当前是否仍只有部分导入预览", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });
    configs[0].previewData = {
      tableIndex: 0,
      headers: ["项目", "规格"],
      rows: [["A", "B"]],
      totalRows: 8,
      columnCount: 2
    };

    expect(getDataImportPreviewLoadState(configs)).toEqual({
      loadedRows: 1,
      totalRows: 8,
      hasPartialPreview: true
    });
  });
});
