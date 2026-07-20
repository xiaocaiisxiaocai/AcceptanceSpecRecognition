import { describe, expect, it } from "vitest";
import {
  applySmartConfigConfirmRequestToTable,
  buildSmartConfigConfirmRequest,
  canConfirmSmartStructureTable,
  canSelectSmartStructureTable,
  createSmartStructureSummary,
  excelColumnLabelToNumber,
  findNearestSmartStructureHeaderRowIndex,
  formatDisplayIndexFromZeroBased,
  formatDisplayRowRange,
  getRecognizedTableInfo,
  getSmartStructureDecisionTag,
  getSmartStructureFieldLabel,
  getSmartStructureImportReadinessReason,
  getSmartStructureImportSelectionDisabledReason,
  needsManualStructureFallback,
  parseExcelA1ColumnRange,
  countSmartStructureRegionRows,
  resolveSmartStructureRegionEndRowIndex,
  shouldShowSmartStructureManualFallback,
  toActualColumnNumber,
  toActualRowNumber,
  toExcelColumnLabel,
  validateSmartStructureRegions
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
  it("从数据区上方向前找到最近有效单行表头并跳过非表头行", () => {
    const rows = [
      ["", "项目", "细项", "规格", "", "", "", "", "厂商确认", ""],
      ["", "项目", "细项", "规格", "", "", "", "", "OK/NG", "Remark"],
      ["", "设备装机", "装机前验机", "", "", "", "", "", "", ""]
    ];

    expect(
      findNearestSmartStructureHeaderRowIndex(rows, [
        { columnIndex: 2, expectedHeader: "细项" },
        { columnIndex: 3, expectedHeader: "规格" },
        { columnIndex: 8, expectedHeader: "厂商确认 / OK/NG" },
        { columnIndex: 9, expectedHeader: "Remark" }
      ])
    ).toBe(1);
  });

  it("不会把字段值完整但与已识别标题不匹配的数据行当成表头", () => {
    expect(
      findNearestSmartStructureHeaderRowIndex(
        [["设备装机", "位置要求", "OK", "无"]],
        [
          { columnIndex: 0, expectedHeader: "项目" },
          { columnIndex: 1, expectedHeader: "规格" },
          { columnIndex: 2, expectedHeader: "OK/NG" },
          { columnIndex: 3, expectedHeader: "Remark" }
        ]
      )
    ).toBeUndefined();
  });

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
      fileId: undefined,
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
      recommendation: "Optional",
      userModifiedStructure: false,
      tableIndex: 0,
      learnedColumns: [
        { header: "项目名称", targetField: 1 },
        { header: "规格内容", targetField: 2 },
        { header: "判定", targetField: 3 }
      ],
      regions: [
        {
          regionId: "table-0-region-0",
          regionIndex: 0,
          headers: ["项目", "规格", "验收", "备注"],
          projectColumnIndex: 0,
          specificationColumnIndex: 1,
          acceptanceColumnIndex: 2,
          remarkColumnIndex: 3,
          headerRowIndex: 0,
          headerRowCount: 1,
          dataStartRowIndex: 1,
          dataEndRowIndex: 8,
          isSpecificationOnly: false
        }
      ]
    });
  });

  it("确认后不会把 Skip 建议固化进模板", () => {
    const source = table({ recommendation: "Skip", decision: "NeedConfirm" });
    const request = buildSmartConfigConfirmRequest(12, source);

    expect(request.recommendation).toBe("Optional");
    expect(
      applySmartConfigConfirmRequestToTable(source, request)
    ).toMatchObject({
      decision: "AutoApply",
      recommendation: "Optional",
      skipReason: undefined
    });
  });

  it("确认请求携带来源文件与表格编号供后端重新提取表头", () => {
    const request = buildSmartConfigConfirmRequest(
      12,
      table({ tableIndex: 3 }),
      { fileId: 88 }
    );

    expect(request).toMatchObject({ fileId: 88, tableIndex: 3 });
  });

  it("确认后保留用户调整和新增的多区域范围", () => {
    const source = table({});
    const request = buildSmartConfigConfirmRequest(12, source);
    request.regions = [
      { ...request.regions![0], dataEndRowIndex: 5 },
      {
        ...request.regions![0],
        regionId: "table-0-region-new-1",
        regionIndex: 1,
        headerRowIndex: 19,
        dataStartRowIndex: 20,
        dataEndRowIndex: 30
      }
    ];

    const updated = applySmartConfigConfirmRequestToTable(source, request);

    expect(updated.regions).toHaveLength(2);
    expect(updated.regions?.map(region => region.dataEndRowIndex)).toEqual([
      5, 30
    ]);
    expect(updated.regions?.[1]).toMatchObject({
      regionId: "table-0-region-new-1",
      regionIndex: 1,
      decision: "AutoApply"
    });
  });

  it("确认后清理已处理的跳过状态和旧结构问题", () => {
    const source = table({
      decision: "Reject",
      recommendation: "Skip",
      skipReason: "命中非业务表路由规则",
      issues: [
        {
          code: "MissingAcceptanceColumn",
          severity: "Error",
          message: "缺少验收列"
        },
        {
          code: "RoutingRule.7",
          severity: "Info",
          message: "建议跳过"
        }
      ],
      regions: [
        {
          regionId: "table-0-region-0",
          regionIndex: 0,
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
          confidence: 0.45,
          source: "Rule",
          decision: "Reject",
          issues: [
            {
              code: "InvalidRowRange",
              severity: "Error",
              message: "数据范围无效"
            }
          ],
          fields: []
        }
      ]
    });
    const request = buildSmartConfigConfirmRequest(12, source, {
      userModifiedStructure: true
    });

    const updated = applySmartConfigConfirmRequestToTable(source, request);

    expect(updated).toMatchObject({
      decision: "AutoApply",
      recommendation: "Optional",
      skipReason: undefined,
      issues: []
    });
    expect(updated.regions?.[0]).toMatchObject({
      decision: "AutoApply",
      issues: []
    });
    expect(canSelectSmartStructureTable(updated)).toBe(true);
  });

  it("Reject 必须发生结构修改后才允许确认", () => {
    const base = {
      readonly: false,
      confirmationLocked: false,
      customerId: 12,
      allRegionsConfirmable: true,
      structureValidationError: ""
    };

    expect(
      canConfirmSmartStructureTable({
        ...base,
        decision: "Reject",
        hasStructureChanges: false
      })
    ).toBe(false);
    expect(
      canConfirmSmartStructureTable({
        ...base,
        decision: "Reject",
        hasStructureChanges: true
      })
    ).toBe(true);
    expect(
      canConfirmSmartStructureTable({
        ...base,
        decision: "NeedConfirm",
        hasStructureChanges: false
      })
    ).toBe(true);
  });

  it("待确认建议在确认成功后归一为可选", () => {
    const source = table({
      decision: "NeedConfirm",
      recommendation: "NeedConfirm"
    });

    const updated = applySmartConfigConfirmRequestToTable(
      source,
      buildSmartConfigConfirmRequest(12, source)
    );

    expect(updated.recommendation).toBe("Optional");
  });

  it("确认请求保证数据起始行位于全部表头之后", () => {
    const request = buildSmartConfigConfirmRequest(
      12,
      table({ headerRowIndex: 7, headerRowCount: 2, dataStartRowIndex: 8 })
    );

    expect(request.dataStartRowIndex).toBe(9);
  });

  it("缺少规格列时拒绝构建确认请求", () => {
    expect(() =>
      buildSmartConfigConfirmRequest(
        12,
        table({ specificationColumnIndex: undefined })
      )
    ).toThrow("规格列不能为空");
  });

  it("缺少验收列时拒绝构建确认请求", () => {
    expect(() =>
      buildSmartConfigConfirmRequest(
        12,
        table({ acceptanceColumnIndex: undefined })
      )
    ).toThrow("验收列不能为空");
  });

  it("缺少备注列时仍允许构建确认请求", () => {
    const request = buildSmartConfigConfirmRequest(
      12,
      table({ remarkColumnIndex: undefined })
    );

    expect(request.remarkColumnIndex).toBeUndefined();
  });

  it("待确认表缺少必填列时仍可手动勾选，但保持待配置状态", () => {
    const pendingTable = table({
      decision: "NeedConfirm",
      recommendation: "Optional",
      projectColumnIndex: undefined,
      acceptanceColumnIndex: undefined
    });

    expect(canSelectSmartStructureTable(pendingTable)).toBe(true);
    expect(getSmartStructureImportSelectionDisabledReason(pendingTable)).toBe(
      ""
    );
    expect(getSmartStructureImportReadinessReason(pendingTable)).toBe(
      "缺少项目列、验收列；请补齐后点击“确认并学习”"
    );
  });

  it("后端明确拒绝或建议跳过的表不可手动勾选", () => {
    const rejectedTable = table({ decision: "Reject" });
    const skippedTable = table({ recommendation: "Skip" });

    expect(canSelectSmartStructureTable(rejectedTable)).toBe(false);
    expect(getSmartStructureImportReadinessReason(rejectedTable)).toBe(
      "后端判定该表不可导入"
    );
    expect(canSelectSmartStructureTable(skippedTable)).toBe(false);
    expect(getSmartStructureImportSelectionDisabledReason(skippedTable)).toBe(
      "后端建议跳过该表"
    );
    expect(needsManualStructureFallback(rejectedTable)).toBe(true);
    expect(needsManualStructureFallback(skippedTable)).toBe(true);
  });

  it("仅在识别结束且存在异常结果时显示手动兜底", () => {
    const normalTables = [
      table({ decision: "AutoApply", recommendation: "Recommended" })
    ];

    expect(
      shouldShowSmartStructureManualFallback({
        recognitionAttempted: false,
        recognizing: false,
        error: "",
        tables: []
      })
    ).toBe(false);
    expect(
      shouldShowSmartStructureManualFallback({
        recognitionAttempted: true,
        recognizing: true,
        error: "",
        tables: []
      })
    ).toBe(false);
    expect(
      shouldShowSmartStructureManualFallback({
        recognitionAttempted: true,
        recognizing: false,
        error: "",
        tables: normalTables
      })
    ).toBe(false);
    expect(
      shouldShowSmartStructureManualFallback({
        recognitionAttempted: true,
        recognizing: false,
        error: "识别失败",
        tables: []
      })
    ).toBe(true);
    expect(
      shouldShowSmartStructureManualFallback({
        recognitionAttempted: true,
        recognizing: false,
        error: "",
        tables: [table({ decision: "Reject" })]
      })
    ).toBe(true);
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

  it("解析并标准化用户直接输入的 Excel 单列 A1 范围", () => {
    expect(parseExcelA1ColumnRange(" c9：c112 ")).toEqual({
      columnNumber: 3,
      startRow: 9,
      endRow: 112,
      normalized: "C9:C112"
    });
    expect(parseExcelA1ColumnRange("$AA$128:$AA$143")).toEqual({
      columnNumber: 27,
      startRow: 128,
      endRow: 143,
      normalized: "AA128:AA143"
    });
    expect(excelColumnLabelToNumber("AA")).toBe(27);
    expect(toExcelColumnLabel(27)).toBe("AA");
  });

  it("拒绝跨列、倒序或不完整的 Excel A1 范围", () => {
    expect(parseExcelA1ColumnRange("C9:D112")).toBeUndefined();
    expect(parseExcelA1ColumnRange("C112:C9")).toBeUndefined();
    expect(parseExcelA1ColumnRange("C9")).toBeUndefined();
  });

  it("按表格实际使用区域换算识别出的行列位置", () => {
    const info = tableInfo();

    expect(toActualRowNumber(info, 0)).toBe(3);
    expect(toActualRowNumber(info, 2)).toBe(5);
    expect(toActualColumnNumber(info, 0)).toBe(2);
    expect(toActualColumnNumber(info, 3)).toBe(5);
    expect(toActualColumnNumber(info, undefined)).toBeUndefined();
  });

  it("将 API 返回的 null 列索引视为缺列", () => {
    const info = tableInfo();

    expect(
      toActualColumnNumber(info, null as unknown as number)
    ).toBeUndefined();
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

  it("开放结束行按工作表末行统计并保持 A1 范围口径一致", () => {
    const region = {
      regionId: "region-0",
      regionIndex: 0,
      headers: ["项目", "规格", "验收", "备注"],
      headerRowIndex: 7,
      headerRowCount: 1,
      dataStartRowIndex: 8,
      dataEndRowIndex: null,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      isSpecificationOnly: false,
      confidence: 0.9,
      source: "Rule",
      decision: "AutoApply" as const,
      fields: []
    };
    const info = tableInfo({ rowCount: 143 });

    expect(resolveSmartStructureRegionEndRowIndex(region, info)).toBe(142);
    expect(countSmartStructureRegionRows([region], info)).toBe(135);
    expect(validateSmartStructureRegions([region], info)).toBe("");
  });

  it("共享区域校验拒绝重复列、越界行和区域重叠", () => {
    const baseRegion = {
      regionId: "region-0",
      regionIndex: 0,
      headers: ["项目", "规格", "验收", "备注"],
      headerRowIndex: 0,
      headerRowCount: 1,
      dataStartRowIndex: 1,
      dataEndRowIndex: 5,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      isSpecificationOnly: false,
      confidence: 0.9,
      source: "Rule",
      decision: "NeedConfirm" as const,
      fields: []
    };
    const info = tableInfo({ rowCount: 20, columnCount: 4 });

    expect(
      validateSmartStructureRegions(
        [{ ...baseRegion, specificationColumnIndex: 0 }],
        info
      )
    ).toContain("不能重复");
    expect(
      validateSmartStructureRegions(
        [{ ...baseRegion, dataEndRowIndex: 20 }],
        info
      )
    ).toContain("范围无效");
    expect(
      validateSmartStructureRegions(
        [
          baseRegion,
          {
            ...baseRegion,
            regionId: "region-1",
            regionIndex: 1,
            headerRowIndex: 5,
            dataStartRowIndex: 6,
            dataEndRowIndex: 10
          }
        ],
        info
      )
    ).toBe("数据区域之间不能重叠");
  });
});
