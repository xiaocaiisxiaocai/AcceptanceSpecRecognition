import { describe, expect, it } from "vitest";
import {
  buildSmartFillConfigsFromRecognizedTables,
  createSmartFillSmartSteps
} from "./smartFill.smartRecognition";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { TableInfo } from "@/api/document";

const tableInfo = (index: number): TableInfo => ({
  index,
  name: `目标表${index + 1}`,
  rowCount: 20,
  columnCount: 5,
  isNested: false,
  headers: ["项目", "规格", "验收", "备注", "其他"],
  hasMergedCells: false,
  usedRangeStartRow: 2,
  usedRangeStartColumn: 3
});

const recognizedTable = (
  overrides: Partial<SmartConfigRecognizedTable>
): SmartConfigRecognizedTable => ({
  tableIndex: 0,
  tableName: "目标表1",
  headers: ["项目", "规格", "验收", "备注"],
  headerRowIndex: 0,
  headerRowCount: 1,
  dataStartRowIndex: 1,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 2,
  remarkColumnIndex: 3,
  isSpecificationOnly: false,
  confidence: 0.94,
  source: "Rule",
  decision: "AutoApply",
  fields: [],
  ...overrides
});

describe("smartFill.smartRecognition", () => {
  it("创建上传/归属、匹配配置、预览确认三步", () => {
    expect(createSmartFillSmartSteps()).toEqual([
      { title: "上传/归属", description: "上传目标文档并选择业务归属" },
      { title: "匹配配置", description: "确认匹配参数" },
      { title: "预览确认", description: "确认匹配结果" }
    ]);
  });

  it("Word 识别结果转为智能填充表格配置", () => {
    const configs = buildSmartFillConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0]).toMatchObject({
      tableIndex: 0,
      selected: true,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowStart: 1,
      headerRowCount: 1,
      dataStartRow: 2,
      mappingAutoDetected: true
    });
  });

  it("Excel 识别结果转为实际 1-based 行列号", () => {
    const configs = buildSmartFillConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });

    expect(configs[0]).toMatchObject({
      projectColumnIndex: 3,
      specificationColumnIndex: 4,
      acceptanceColumnIndex: 5,
      remarkColumnIndex: 6,
      headerRowStart: 2,
      headerRowCount: 1,
      dataStartRow: 3
    });
  });

  it("仅规格模式允许缺少项目列并跳过不可用表", () => {
    const configs = buildSmartFillConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [
        recognizedTable({
          tableIndex: 0,
          decision: "Reject"
        }),
        recognizedTable({
          tableIndex: 1,
          projectColumnIndex: undefined,
          isSpecificationOnly: true
        })
      ],
      tableInfos: [tableInfo(0), tableInfo(1)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0]).toMatchObject({
      tableIndex: 1,
      projectColumnIndex: 0,
      specificationColumnIndex: 1
    });
  });
});
