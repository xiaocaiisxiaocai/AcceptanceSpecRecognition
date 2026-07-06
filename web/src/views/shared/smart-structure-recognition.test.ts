import { describe, expect, it } from "vitest";
import {
  buildSmartConfigConfirmRequest,
  createSmartStructureSummary,
  formatDisplayIndexFromZeroBased,
  formatDisplayRowRange,
  getRecognizedTableInfo,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel,
  toActualColumnNumber,
  toActualRowNumber
} from "./smart-structure-recognition";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";
import type { TableInfo } from "@/api/document";

const table = (
  overrides: Partial<SmartConfigRecognizedTable>
): SmartConfigRecognizedTable => ({
  tableIndex: 0,
  tableName: "验收表",
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
  confidence: 0.96,
  source: "Rule",
  decision: "AutoApply",
  fields: [],
  ...overrides
});

const tableInfo = (overrides: Partial<TableInfo> = {}): TableInfo => ({
  index: 0,
  name: "验收表",
  rowCount: 10,
  columnCount: 6,
  isNested: false,
  headers: ["规格", "验收", "备注"],
  hasMergedCells: false,
  usedRangeStartRow: 3,
  usedRangeStartColumn: 2,
  ...overrides
});

describe("smart-structure-recognition", () => {
  it("统计智能结构识别结果摘要", () => {
    const summary = createSmartStructureSummary([
      table({ decision: "AutoApply" }),
      table({ tableIndex: 1, decision: "NeedConfirm", confidence: 0.62 }),
      table({ tableIndex: 2, decision: "Reject", confidence: 0.25 })
    ]);

    expect(summary).toEqual({
      total: 3,
      autoApply: 1,
      needConfirm: 1,
      reject: 1,
      recommended: 0,
      optional: 0,
      skip: 0,
      averageConfidence: 0.61,
      canAutoApplyAll: false,
      hasNeedConfirm: true,
      hasReject: true
    });
  });

  it("空结果摘要保持可展示的零值", () => {
    expect(createSmartStructureSummary([])).toEqual({
      total: 0,
      autoApply: 0,
      needConfirm: 0,
      reject: 0,
      recommended: 0,
      optional: 0,
      skip: 0,
      averageConfidence: 0,
      canAutoApplyAll: false,
      hasNeedConfirm: false,
      hasReject: false
    });
  });

  it("从识别表构建确认学习请求", () => {
    const request = buildSmartConfigConfirmRequest(
      12,
      table({
        tableName: "A客户模板",
        projectColumnIndex: 0,
        specificationColumnIndex: 1,
        acceptanceColumnIndex: 2,
        remarkColumnIndex: 3,
        fields: [
          {
            field: "Project",
            columnIndex: 0,
            header: "项目名称",
            confidence: 0.9,
            source: "Rule"
          },
          {
            field: "Specification",
            columnIndex: 1,
            header: "规格内容",
            confidence: 0.95,
            source: "Rule"
          },
          {
            field: "Acceptance",
            columnIndex: 2,
            header: "判定",
            confidence: 0.88,
            source: "Rule"
          }
        ]
      })
    );

    expect(request).toEqual({
      customerId: 12,
      templateName: "A客户模板",
      headers: ["项目", "规格", "验收", "备注"],
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 8,
      isSpecificationOnly: false,
      tableKind: undefined,
      recommendation: undefined,
      userModifiedStructure: false,
      learnedColumns: [
        { header: "项目名称", targetField: 1 },
        { header: "规格内容", targetField: 2 },
        { header: "判定", targetField: 3 }
      ]
    });
  });

  it("缺少规格列时拒绝构建确认请求", () => {
    expect(() =>
      buildSmartConfigConfirmRequest(
        12,
        table({ specificationColumnIndex: undefined })
      )
    ).toThrow("规格列不能为空");
  });

  it("缺少验收列或备注列时拒绝构建确认请求", () => {
    expect(() =>
      buildSmartConfigConfirmRequest(
        12,
        table({ acceptanceColumnIndex: undefined })
      )
    ).toThrow("验收列不能为空");

    expect(() =>
      buildSmartConfigConfirmRequest(12, table({ remarkColumnIndex: undefined }))
    ).toThrow("备注列不能为空");
  });

  it("转换字段和决策展示标签", () => {
    expect(getSmartStructureFieldLabel("Project")).toBe("项目");
    expect(getSmartStructureFieldLabel("Specification")).toBe("规格");
    expect(getSmartStructureDecisionTag("AutoApply")).toEqual({
      text: "可直达",
      type: "success"
    });
    expect(getSmartStructureDecisionTag("NeedConfirm")).toEqual({
      text: "待确认",
      type: "warning"
    });
    expect(getSmartStructureDecisionTag("Reject")).toEqual({
      text: "不可用",
      type: "danger"
    });
  });

  it("行列索引展示给用户时从 1 开始", () => {
    expect(formatDisplayIndexFromZeroBased(0)).toBe(1);
    expect(formatDisplayIndexFromZeroBased(3)).toBe(4);
    expect(formatDisplayIndexFromZeroBased(undefined)).toBe("-");
    expect(
      formatDisplayRowRange({
        headerRowIndex: 0,
        dataStartRowIndex: 1
      })
    ).toBe("表头 1 / 数据 2");
  });

  it("按表格实际使用区域换算识别出的行列位置", () => {
    const info = tableInfo();

    expect(toActualRowNumber(info, 0)).toBe(3);
    expect(toActualRowNumber(info, 2)).toBe(5);
    expect(toActualColumnNumber(info, 0)).toBe(2);
    expect(toActualColumnNumber(info, 3)).toBe(5);
    expect(toActualColumnNumber(info, undefined)).toBeUndefined();
  });

  it("缺少表格区域信息时按 1 起始换算行列位置", () => {
    expect(toActualRowNumber(undefined, 0)).toBe(1);
    expect(toActualColumnNumber(undefined, 0)).toBe(1);
  });

  it("按识别表索引查找对应表格信息", () => {
    expect(
      getRecognizedTableInfo(
        [tableInfo({ index: 2, name: "第二张表" })],
        table({ tableIndex: 2 })
      )?.name
    ).toBe("第二张表");
  });
});
