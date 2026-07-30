import { describe, expect, it } from "vitest";
import type { SmartConfigRecognizedRegion } from "../../api/smart-config";
import {
  applySmartStructureExcelA1Patch,
  applySmartStructureExcelColumnPatch,
  applySmartStructureExcelEndpointPatch,
  applySmartStructureExcelRowPatch,
  createSmartStructureExcelRegionDraft,
  formatSmartStructureExcelFieldEndpoints,
  formatSmartStructureExcelFieldRange,
  getSmartStructureExcelRowInputLimits,
  normalizeSmartStructureInlineExcelRegion,
  resolveSmartStructureExcelBlockingValidationError,
  setSmartStructureSpecificationOnly,
  toSmartConfigRecognizedRegion,
  validateSmartStructureExcelRegionDrafts,
  type SmartStructureExcelRegionBounds
} from "./smart-structure-region-draft";

const bounds: SmartStructureExcelRegionBounds = {
  baseRow: 5,
  baseColumn: 2,
  rowCount: 50,
  columnCount: 8
};

const createRegion = (
  overrides: Partial<SmartConfigRecognizedRegion> = {}
): SmartConfigRecognizedRegion => ({
  regionId: "region-a",
  regionIndex: 0,
  headers: [
    "项目",
    "规格",
    "供应商",
    "验收",
    "备注",
    "扩展 1",
    "扩展 2",
    "扩展 3"
  ],
  headerRowIndex: 1,
  headerRowCount: 1,
  dataStartRowIndex: 2,
  dataEndRowIndex: 10,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex: 3,
  remarkColumnIndex: 4,
  isSpecificationOnly: false,
  confidence: 0.86,
  source: "Llm",
  decision: "NeedConfirm",
  issues: [{ code: "review", severity: "Warning", message: "待确认" }],
  fields: [
    {
      field: "Project",
      columnIndex: 0,
      header: "项目",
      confidence: 0.8,
      source: "Rule"
    },
    {
      field: "Specification",
      columnIndex: 1,
      header: "规格",
      confidence: 0.9,
      source: "Llm"
    },
    {
      field: "Acceptance",
      columnIndex: 3,
      header: "验收",
      confidence: 0.7,
      source: "Fused"
    }
  ],
  ...overrides
});

describe("smart structure Excel region draft", () => {
  it("只让A1和结构错误阻断确认，端点内容预览状态不属于提交契约", () => {
    const draft = createSmartStructureExcelRegionDraft(createRegion(), bounds);

    expect(
      resolveSmartStructureExcelBlockingValidationError([draft], {}, bounds)
    ).toBe("");
    expect(
      resolveSmartStructureExcelBlockingValidationError(
        [draft],
        {
          [draft.regionId]: {
            project: { start: "项目起始单元格地址无效" }
          }
        },
        bounds
      )
    ).toBe("区域 1：项目起始单元格地址无效");
  });

  it("把相对 0-based 识别区域转换成绝对 1-based 单一草稿", () => {
    const source = createRegion();
    const draft = createSmartStructureExcelRegionDraft(source, bounds);

    expect(draft).toMatchObject({
      regionId: "region-a",
      headerStartRow: 6,
      headerRowCount: 1,
      dataStartRow: 7,
      dataEndRow: 15,
      projectColumn: 2,
      specificationColumn: 3,
      acceptanceColumn: 5,
      remarkColumn: 6,
      isSpecificationOnly: false
    });
    expect(formatSmartStructureExcelFieldRange(draft, "project")).toBe(
      "B7:B15"
    );
    expect(formatSmartStructureExcelFieldRange(draft, "specification")).toBe(
      "C7:C15"
    );
    expect(formatSmartStructureExcelFieldRange(draft, "acceptance")).toBe(
      "E7:E15"
    );
    expect(formatSmartStructureExcelFieldRange(draft, "remark")).toBe("F7:F15");

    draft.source.headers[0] = "已修改";
    expect(source.headers[0]).toBe("项目");
  });

  it("初始化时忽略识别出的多行表头，固定取数据起始行上一行", () => {
    const screenshotBounds = { ...bounds, baseRow: 1, rowCount: 10 };
    const draft = createSmartStructureExcelRegionDraft(
      createRegion({
        headerRowIndex: 0,
        headerRowCount: 5,
        dataStartRowIndex: 5,
        dataEndRowIndex: 9
      }),
      screenshotBounds
    );

    expect(draft).toMatchObject({
      headerStartRow: 5,
      headerRowCount: 1,
      dataStartRow: 6,
      dataEndRow: 10
    });
    expect(formatSmartStructureExcelFieldRange(draft, "project")).toBe(
      "B6:B10"
    );
    expect(
      toSmartConfigRecognizedRegion(draft, screenshotBounds)
    ).toMatchObject({
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 5,
      dataEndRowIndex: 9
    });
  });

  it("内联 Excel 区域在进入父级草稿时也固定为上一行单行表头", () => {
    const source = createRegion({
      headerRowIndex: 1,
      headerRowCount: 3,
      dataStartRowIndex: 6,
      dataEndRowIndex: 9
    });

    expect(normalizeSmartStructureInlineExcelRegion(source)).toMatchObject({
      headerRowIndex: 5,
      headerRowCount: 1,
      dataStartRowIndex: 6,
      dataEndRowIndex: 9
    });
    expect(source).toMatchObject({
      headerRowIndex: 1,
      headerRowCount: 3
    });
  });

  it("行修改返回新草稿，并让所有字段 A1 使用共享数据行", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const updated = applySmartStructureExcelRowPatch(
      original,
      {
        dataStartRow: 9,
        dataEndRow: 20
      },
      bounds
    );

    expect(updated).not.toBe(original);
    expect(original.dataStartRow).toBe(7);
    expect(updated.headerStartRow).toBe(8);
    expect(updated.headerRowCount).toBe(1);
    expect(formatSmartStructureExcelFieldRange(updated, "project")).toBe(
      "B9:B20"
    );
    expect(formatSmartStructureExcelFieldRange(updated, "specification")).toBe(
      "C9:C20"
    );
    expect(formatSmartStructureExcelFieldRange(updated, "acceptance")).toBe(
      "E9:E20"
    );
    expect(formatSmartStructureExcelFieldRange(updated, "remark")).toBe(
      "F9:F20"
    );
  });

  it("修改数据起始行时原子重算上一行表头并保证结束行不反向", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const movedData = applySmartStructureExcelRowPatch(
      original,
      {
        dataStartRow: 16
      },
      bounds
    );

    expect(movedData).toMatchObject({
      headerStartRow: 15,
      headerRowCount: 1,
      dataStartRow: 16,
      dataEndRow: 16
    });
    expect(formatSmartStructureExcelFieldRange(movedData, "project")).toBe(
      "B16:B16"
    );
  });

  it("数据起始行至少保留工作表首行作为表头", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const updated = applySmartStructureExcelRowPatch(
      original,
      {
        dataStartRow: 5,
        dataEndRow: 5
      },
      bounds
    );

    expect(updated.headerStartRow).toBe(5);
    expect(updated.headerRowCount).toBe(1);
    expect(updated.dataStartRow).toBe(6);
    expect(updated.dataEndRow).toBe(6);
  });

  it("识别行数尚未加载或范围暂时越界时输入框边界始终合法", () => {
    const oneRowBounds = { ...bounds, rowCount: 1 };
    const draft = {
      ...createSmartStructureExcelRegionDraft(createRegion(), oneRowBounds),
      dataStartRow: 9,
      dataEndRow: 12
    };

    const limits = getSmartStructureExcelRowInputLimits(draft, oneRowBounds);

    expect(limits).toEqual({
      dataStartMinimum: 6,
      dataStartMaximum: 9,
      dataEndMinimum: 9,
      dataEndMaximum: 12
    });
    expect(limits.dataStartMinimum).toBeLessThanOrEqual(
      limits.dataStartMaximum
    );
    expect(limits.dataEndMinimum).toBeLessThanOrEqual(limits.dataEndMaximum);
  });

  it("列修改只改变目标字段列及其派生 A1", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const updated = applySmartStructureExcelColumnPatch(
      original,
      "specification",
      7
    );

    expect(updated).not.toBe(original);
    expect(updated.specificationColumn).toBe(7);
    expect(updated.projectColumn).toBe(2);
    expect(formatSmartStructureExcelFieldRange(updated, "specification")).toBe(
      "G7:G15"
    );
    expect(formatSmartStructureExcelFieldRange(updated, "project")).toBe(
      "B7:B15"
    );
  });

  it("完整合法 A1 原子更新目标列和共享数据行", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const result = applySmartStructureExcelA1Patch(
      original,
      "project",
      " $H$9：$H$20 ",
      bounds
    );

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result).toMatchObject({
      normalizedRange: "H9:H20",
      synchronizedRows: true
    });
    expect(result.draft).not.toBe(original);
    expect(result.draft.projectColumn).toBe(8);
    expect(result.draft.headerStartRow).toBe(8);
    expect(result.draft.headerRowCount).toBe(1);
    expect(result.draft.dataStartRow).toBe(9);
    expect(result.draft.dataEndRow).toBe(20);
    expect(
      formatSmartStructureExcelFieldRange(result.draft, "specification")
    ).toBe("C9:C20");
    expect(
      formatSmartStructureExcelFieldRange(result.draft, "acceptance")
    ).toBe("E9:E20");
    expect(original).toMatchObject({
      projectColumn: 2,
      dataStartRow: 7,
      dataEndRow: 15
    });
  });

  it("拆分起止单元格后仍原子更新字段列和共享数据行", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );

    expect(
      formatSmartStructureExcelFieldEndpoints(original, "project")
    ).toEqual({
      start: "B7",
      end: "B15"
    });

    const startResult = applySmartStructureExcelEndpointPatch(
      original,
      "project",
      "start",
      " $H$9 ",
      bounds
    );
    expect(startResult.ok).toBe(true);
    if (!startResult.ok) return;
    expect(startResult).toMatchObject({
      normalizedRange: "H9:H15",
      synchronizedRows: true
    });
    expect(startResult.draft).toMatchObject({
      projectColumn: 8,
      headerStartRow: 8,
      dataStartRow: 9,
      dataEndRow: 15
    });
    expect(
      formatSmartStructureExcelFieldEndpoints(
        startResult.draft,
        "specification"
      )
    ).toEqual({ start: "C9", end: "C15" });

    const endResult = applySmartStructureExcelEndpointPatch(
      startResult.draft,
      "project",
      "end",
      "H20",
      bounds
    );
    expect(endResult.ok).toBe(true);
    if (!endResult.ok) return;
    expect(endResult.draft).toMatchObject({
      projectColumn: 8,
      dataStartRow: 9,
      dataEndRow: 20
    });
    expect(
      formatSmartStructureExcelFieldRange(endResult.draft, "project")
    ).toBe("H9:H20");
  });

  it("拆分端点只接受单个 A1 单元格且拒绝任一反向端点", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );

    const rangeInput = applySmartStructureExcelEndpointPatch(
      original,
      "project",
      "start",
      "H9:H20",
      bounds
    );
    expect(rangeInput).toEqual({
      ok: false,
      error: "起始单元格必须使用 A1 格式，例如 C9"
    });

    const reversedEnd = applySmartStructureExcelEndpointPatch(
      original,
      "project",
      "end",
      "H6",
      bounds
    );
    expect(reversedEnd).toEqual({
      ok: false,
      error: "结束单元格不能早于起始单元格"
    });

    const reversedStart = applySmartStructureExcelEndpointPatch(
      original,
      "project",
      "start",
      "H20",
      bounds
    );
    expect(reversedStart).toEqual({
      ok: false,
      error: "起始单元格不能晚于结束单元格"
    });
  });

  it("A1 数据范围向上移动时表头跟随到新的上一行", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const result = applySmartStructureExcelA1Patch(
      original,
      "project",
      "B6:B12",
      bounds
    );

    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.draft).toMatchObject({
      headerStartRow: 5,
      headerRowCount: 1,
      dataStartRow: 6,
      dataEndRow: 12
    });
  });

  it.each([
    ["H20:G9", "同一列"],
    ["A9:A20", "列范围"],
    ["H1:H20", "表头"],
    ["H9:H99", "行范围"]
  ])("无效 A1 %s 不修改草稿", (value, errorPart) => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const before = structuredClone(original);
    const result = applySmartStructureExcelA1Patch(
      original,
      "project",
      value,
      bounds
    );

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.error).toContain(errorPart);
    expect(original).toEqual(before);
  });

  it("仅规格模式只能由显式开关改变，开启后清除项目列", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const columnPatched = applySmartStructureExcelColumnPatch(
      original,
      "project",
      undefined
    );

    expect(columnPatched.isSpecificationOnly).toBe(false);
    expect(
      validateSmartStructureExcelRegionDrafts([columnPatched], bounds)
    ).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          regionId: "region-a",
          field: "projectColumn",
          code: "project-required"
        })
      ])
    );

    const specificationOnly = setSmartStructureSpecificationOnly(
      original,
      true
    );
    expect(specificationOnly).not.toBe(original);
    expect(specificationOnly.isSpecificationOnly).toBe(true);
    expect(specificationOnly.projectColumn).toBeUndefined();
    expect(
      validateSmartStructureExcelRegionDrafts([specificationOnly], bounds)
    ).toEqual([]);
  });

  it("备注列缺失时应阻止提交并定位到备注列", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const missingRemark = applySmartStructureExcelColumnPatch(
      original,
      "remark",
      undefined
    );

    expect(
      validateSmartStructureExcelRegionDrafts([missingRemark], bounds)
    ).toEqual(
      expect.arrayContaining([
        expect.objectContaining({
          regionId: "region-a",
          field: "remarkColumn",
          code: "remark-required",
          message: "必须选择备注列"
        })
      ])
    );
  });

  it("校验表头、数据、必填列、重复列和工作表越界", () => {
    const original = createSmartStructureExcelRegionDraft(
      createRegion(),
      bounds
    );
    const invalid = {
      ...original,
      headerStartRow: 4,
      headerRowCount: 0,
      dataStartRow: 5,
      dataEndRow: 60,
      projectColumn: undefined,
      specificationColumn: 3,
      acceptanceColumn: 3,
      remarkColumn: 10
    };
    const issues = validateSmartStructureExcelRegionDrafts([invalid], bounds);

    expect(issues).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ code: "header-out-of-bounds" }),
        expect.objectContaining({ code: "header-row-count" }),
        expect.objectContaining({ code: "data-out-of-bounds" }),
        expect.objectContaining({ code: "project-required" }),
        expect.objectContaining({ code: "duplicate-column" }),
        expect.objectContaining({ code: "column-out-of-bounds" })
      ])
    );
  });

  it("校验同一工作表内逻辑区域重叠并定位两个区域", () => {
    const first = createSmartStructureExcelRegionDraft(createRegion(), bounds);
    const second = {
      ...createSmartStructureExcelRegionDraft(
        createRegion({
          regionId: "region-b",
          regionIndex: 1,
          headerRowIndex: 9,
          dataStartRowIndex: 10,
          dataEndRowIndex: 20
        }),
        bounds
      )
    };

    const issues = validateSmartStructureExcelRegionDrafts(
      [first, second],
      bounds
    ).filter(issue => issue.code === "region-overlap");

    expect(issues.map(issue => issue.regionId)).toEqual([
      "region-a",
      "region-b"
    ]);
  });

  it("转回识别区域时保留元数据、转回相对索引并重建字段", () => {
    const source = createRegion();
    const draft = applySmartStructureExcelColumnPatch(
      applySmartStructureExcelRowPatch(
        createSmartStructureExcelRegionDraft(source, bounds),
        {
          dataStartRow: 10,
          dataEndRow: 25
        },
        bounds
      ),
      "specification",
      7
    );
    const region = toSmartConfigRecognizedRegion(draft, bounds, 4);

    expect(region).toMatchObject({
      regionId: "region-a",
      regionIndex: 4,
      headerRowIndex: 4,
      headerRowCount: 1,
      dataStartRowIndex: 5,
      dataEndRowIndex: 20,
      projectColumnIndex: 0,
      specificationColumnIndex: 5,
      acceptanceColumnIndex: 3,
      remarkColumnIndex: 4,
      source: "Llm",
      confidence: 0.86,
      decision: "NeedConfirm"
    });
    expect(region.issues).toEqual(source.issues);
    expect(region.fields).toEqual([
      {
        field: "Project",
        columnIndex: 0,
        header: "项目",
        confidence: 0.8,
        source: "Rule"
      },
      {
        field: "Specification",
        columnIndex: 5,
        header: "扩展 1",
        confidence: 0.9,
        source: "Llm"
      },
      {
        field: "Acceptance",
        columnIndex: 3,
        header: "验收",
        confidence: 0.7,
        source: "Fused"
      },
      {
        field: "Remark",
        columnIndex: 4,
        header: "备注",
        confidence: 1,
        source: "Manual"
      }
    ]);
  });
});
