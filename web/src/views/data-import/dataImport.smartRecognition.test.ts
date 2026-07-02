import { describe, expect, it } from "vitest";
import {
  buildDataImportConfigsFromRecognizedTables,
  createDataImportSmartSteps,
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
  });

  it("跳过 Reject 和缺少规格列的表", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [
        recognizedTable({ tableIndex: 0, decision: "Reject" }),
        recognizedTable({
          tableIndex: 1,
          decision: "NeedConfirm",
          specificationColumnIndex: undefined
        }),
        recognizedTable({ tableIndex: 2, decision: "NeedConfirm" })
      ],
      tableInfos: [tableInfo(0), tableInfo(1), tableInfo(2)]
    });

    expect(configs.map(item => item.tableIndex)).toEqual([2]);
  });

  it("高级步骤仅允许进入旧表格选择或映射步骤", () => {
    expect(getDataImportAdvancedStep("tableSelect")).toBe(1);
    expect(getDataImportAdvancedStep("mapping")).toBe(2);
  });
});
