import { describe, expect, it } from "vitest";
import {
  buildSmartConfigConfirmRequest,
  createSmartStructureSummary,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel
} from "./smart-structure-recognition";
import type { SmartConfigRecognizedTable } from "@/api/smart-config";

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
        remarkColumnIndex: undefined,
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
      remarkColumnIndex: undefined,
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 8,
      isSpecificationOnly: false,
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
});
