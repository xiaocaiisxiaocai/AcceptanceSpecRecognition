import { describe, expect, it } from "vitest";
import {
  buildDataImportPreviewStageText,
  buildDataImportConfigsFromRecognizedTables,
  buildManualDataImportConfig,
  canSmartTableBeImported,
  createDataImportSmartSteps,
  createDefaultSelectedSmartTableIndexes,
  filterSelectedSmartTables,
  getDataImportPreviewLoadState,
  getDataImportPreviewTotalCount,
  getDataImportPrevStepState,
  getDataImportAdvancedStep,
  syncDataImportConfigsToRecognizedTables
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
      { title: "上传/目标" },
      { title: "确认/预览" },
      { title: "完成" }
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
      regionId: "table-0-region-0",
      regionIndex: 0,
      projectColumn: 2,
      specificationColumn: 3,
      acceptanceColumn: 4,
      remarkColumn: 5,
      headerRowStart: 3,
      headerRowCount: 1,
      dataStartRow: 4,
      dataEndRow: 11,
      isSpecificationOnly: false
    });
    expect(configs[0].recognizedExcelMapping).toEqual(configs[0].excelMapping);
  });

  it("高级 Excel 映射会同步回识别区域，保持摘要、学习和执行同源", () => {
    const info = tableInfo(0);
    const source = recognizedTable({
      regions: [
        {
          regionId: "r1",
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
          confidence: 0.9,
          source: "Rule",
          decision: "NeedConfirm",
          fields: []
        }
      ]
    });
    const config = buildManualDataImportConfig({
      isExcelFile: true,
      tableInfo: info
    });
    config.excelMapping = {
      projectColumn: 4,
      specificationColumn: 5,
      acceptanceColumn: 3,
      remarkColumn: 2,
      headerRowStart: 8,
      headerRowCount: 2,
      dataStartRow: 10,
      dataEndRow: 14
    };

    const [synced] = syncDataImportConfigsToRecognizedTables({
      isExcelFile: true,
      tables: [source],
      configs: [config]
    });

    expect(synced).toMatchObject({
      projectColumnIndex: 2,
      specificationColumnIndex: 3,
      acceptanceColumnIndex: 1,
      remarkColumnIndex: 0,
      headerRowIndex: 5,
      headerRowCount: 2,
      dataStartRowIndex: 7,
      dataEndRowIndex: 11
    });
    expect(synced.regions).toHaveLength(1);
    expect(synced.regions?.[0]).toMatchObject({
      projectColumnIndex: 2,
      headerRowIndex: 5,
      dataEndRowIndex: 11
    });
  });

  it("Word 仅规格识别结果缺少项目列时仍可生成导入配置", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [
        recognizedTable({
          projectColumnIndex: undefined,
          isSpecificationOnly: true
        })
      ],
      tableInfos: [tableInfo(0)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0]).toMatchObject({
      isSpecificationOnly: true,
      wordMapping: {
        projectColumn: undefined,
        specificationColumn: 1
      }
    });
  });

  it("Excel 仅规格识别结果缺少项目列时保留仅规格标记并不补项目列", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [
        recognizedTable({
          projectColumnIndex: undefined,
          isSpecificationOnly: true
        })
      ],
      tableInfos: [tableInfo(0)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0].isSpecificationOnly).toBe(true);
    expect(configs[0].excelMapping?.projectColumn).toBeUndefined();
    expect(configs[0].excelMapping?.specificationColumn).toBe(3);
  });

  it("Excel 仅规格 API 返回 null 项目列且 confidence 为 0 时仍不补项目列", () => {
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [
        recognizedTable({
          projectColumnIndex: null as unknown as number,
          isSpecificationOnly: true,
          confidence: 0
        })
      ],
      tableInfos: [tableInfo(0)]
    });

    expect(configs).toHaveLength(1);
    expect(configs[0].isSpecificationOnly).toBe(true);
    expect(configs[0].excelMapping?.projectColumn).toBeUndefined();
    expect(configs[0].excelMapping?.specificationColumn).toBe(3);
  });

  it("跳过 Reject 和缺少项目、规格或验收必填列的表，备注列保持可选", () => {
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
        recognizedTable({
          tableIndex: 5,
          decision: "NeedConfirm",
          acceptanceColumnIndex: null as unknown as number
        }),
        recognizedTable({ tableIndex: 2, decision: "NeedConfirm" })
      ],
      tableInfos: [
        tableInfo(0),
        tableInfo(1),
        tableInfo(2),
        tableInfo(3),
        tableInfo(4),
        tableInfo(5)
      ]
    });

    expect(configs.map(item => item.tableIndex)).toEqual([2, 4]);
  });

  it("手动勾选不等于已具备导入条件", () => {
    expect(
      canSmartTableBeImported(
        recognizedTable({
          decision: "NeedConfirm",
          projectColumnIndex: undefined,
          acceptanceColumnIndex: undefined
        })
      )
    ).toBe(false);
    expect(
      canSmartTableBeImported(
        recognizedTable({
          decision: "NeedConfirm",
          remarkColumnIndex: undefined
        })
      )
    ).toBe(true);
  });

  it("默认选中后端推荐且结构完整的表，包括需要确认的多区域表", () => {
    expect(
      createDefaultSelectedSmartTableIndexes([
        recognizedTable({
          tableIndex: 0,
          decision: "AutoApply",
          recommendation: "Recommended"
        }),
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
        recognizedTable({
          tableIndex: 7,
          decision: "NeedConfirm",
          acceptanceColumnIndex: null as unknown as number
        }),
        recognizedTable({
          tableIndex: 8,
          decision: "NeedConfirm",
          projectColumnIndex: null as unknown as number,
          isSpecificationOnly: true,
          confidence: 0
        }),
        recognizedTable({
          tableIndex: 9,
          decision: "AutoApply",
          projectColumnIndex: null as unknown as number,
          isSpecificationOnly: true,
          confidence: 0
        }),
        recognizedTable({ tableIndex: 2, decision: "Reject" }),
        recognizedTable({ tableIndex: 3, decision: "NeedConfirm" }),
        recognizedTable({
          tableIndex: 10,
          decision: "NeedConfirm",
          recommendation: "NeedConfirm"
        })
      ])
    ).toEqual([0, 10]);
  });

  it("仅规格待确认表可在用户手动选择后生成导入配置", () => {
    const pending = recognizedTable({
      decision: "NeedConfirm",
      projectColumnIndex: null,
      isSpecificationOnly: true,
      confidence: 0
    });

    expect(
      buildDataImportConfigsFromRecognizedTables({
        isExcelFile: false,
        tables: [pending],
        tableInfos: [tableInfo(0)]
      })
    ).toHaveLength(1);

    expect(
      buildDataImportConfigsFromRecognizedTables({
        isExcelFile: false,
        tables: [{ ...pending, decision: "AutoApply" }],
        tableInfos: [tableInfo(0)]
      })
    ).toHaveLength(1);
  });

  it("按用户勾选过滤需要导入的识别表", () => {
    const tables = [
      recognizedTable({ tableIndex: 0 }),
      recognizedTable({ tableIndex: 1 }),
      recognizedTable({ tableIndex: 2 })
    ];

    expect(
      filterSelectedSmartTables(tables, [2, 0]).map(t => t.tableIndex)
    ).toEqual([0, 2]);
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
  it("Excel 多区域识别结果保留每段绝对行列范围", () => {
    const base = recognizedTable({});
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [
        recognizedTable({
          regions: [
            {
              regionId: "table-0-region-0",
              regionIndex: 0,
              headers: base.headers,
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
              fields: []
            },
            {
              regionId: "table-0-region-1",
              regionIndex: 1,
              headers: base.headers,
              headerRowIndex: 10,
              headerRowCount: 2,
              dataStartRowIndex: 12,
              dataEndRowIndex: 15,
              projectColumnIndex: 0,
              specificationColumnIndex: 1,
              acceptanceColumnIndex: 2,
              remarkColumnIndex: 3,
              isSpecificationOnly: false,
              confidence: 0.88,
              source: "RepeatedHeader",
              decision: "NeedConfirm",
              fields: []
            }
          ]
        })
      ],
      tableInfos: [{ ...tableInfo(0), rowCount: 20 }]
    });

    expect(configs[0].recognizedExcelMappings).toEqual([
      expect.objectContaining({
        projectColumn: 2,
        dataStartRow: 4,
        dataEndRow: 11
      }),
      expect.objectContaining({
        headerRowStart: 13,
        headerRowCount: 2,
        dataStartRow: 15,
        dataEndRow: 18
      })
    ]);
  });

  it("Word 多区域识别结果保留每段闭区间并同步回识别结构", () => {
    const base = recognizedTable({});
    const source = recognizedTable({
      regions: [
        {
          ...base,
          regionId: "word-region-0",
          regionIndex: 0,
          headers: base.headers,
          dataEndRowIndex: 8
        },
        {
          ...base,
          regionId: "word-region-1",
          regionIndex: 1,
          headers: base.headers,
          headerRowIndex: 10,
          headerRowCount: 2,
          dataStartRowIndex: 12,
          dataEndRowIndex: 15
        }
      ]
    });
    const configs = buildDataImportConfigsFromRecognizedTables({
      isExcelFile: false,
      tables: [source],
      tableInfos: [tableInfo(0)]
    });

    expect(configs[0].recognizedWordMappings).toEqual([
      expect.objectContaining({
        regionId: "word-region-0",
        dataStartRowIndex: 1,
        dataEndRowIndex: 8
      }),
      expect.objectContaining({
        regionId: "word-region-1",
        headerRowCount: 2,
        dataStartRowIndex: 12,
        dataEndRowIndex: 15
      })
    ]);
    const [synced] = syncDataImportConfigsToRecognizedTables({
      isExcelFile: false,
      tables: [source],
      configs
    });
    expect(synced.regions).toHaveLength(2);
    expect(synced.regions?.[1]).toMatchObject({
      regionId: "word-region-1",
      headerRowCount: 2,
      dataEndRowIndex: 15
    });
  });
});
