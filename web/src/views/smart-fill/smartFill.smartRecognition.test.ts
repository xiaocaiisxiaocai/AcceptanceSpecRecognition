import { describe, expect, it } from "vitest";
import {
  buildSmartFillConfigsFromRecognizedTables,
  canContinueFromSmartRecognition,
  createSmartFillSmartSteps,
  getSmartFillPrevStepState,
  shouldSelectSmartFillTableByDefault
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
  recommendation: "Recommended",
  fields: [],
  ...overrides
});

describe("smartFill.smartRecognition", () => {
  it("创建上传/归属、匹配配置、预览确认三步", () => {
    expect(createSmartFillSmartSteps()).toEqual([
      { title: "上传/归属" },
      { title: "匹配配置" },
      { title: "预览确认" }
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

  it("仅后端明确判定可直达且推荐的表才默认勾选", () => {
    expect(
      shouldSelectSmartFillTableByDefault(
        recognizedTable({
          decision: "AutoApply",
          recommendation: "Recommended",
          confidence: 0.94
        })
      )
    ).toBe(true);
    expect(
      shouldSelectSmartFillTableByDefault(
        recognizedTable({
          decision: "NeedConfirm",
          recommendation: "Optional",
          confidence: 0
        })
      )
    ).toBe(false);
    expect(
      shouldSelectSmartFillTableByDefault(
        recognizedTable({
          decision: "AutoApply",
          recommendation: "Recommended",
          confidence: 0
        })
      )
    ).toBe(false);
  });

  it("Excel 匹配列保持 0-based 相对索引，仅行号转为工作表绝对行号", () => {
    const configs = buildSmartFillConfigsFromRecognizedTables({
      isExcelFile: true,
      tables: [recognizedTable({})],
      tableInfos: [tableInfo(0)]
    });

    expect(configs[0]).toMatchObject({
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowStart: 2,
      headerRowCount: 1,
      dataStartRow: 3
    });
  });

  it("识别结果应留在当前页复核，只有全部无需确认时才允许进入匹配配置", () => {
    expect(canContinueFromSmartRecognition([recognizedTable({})], [0])).toBe(
      true
    );
    expect(
      canContinueFromSmartRecognition(
        [recognizedTable({ decision: "NeedConfirm" })],
        [0]
      )
    ).toBe(false);
    expect(canContinueFromSmartRecognition([recognizedTable({})], [])).toBe(
      false
    );
  });

  it("取消勾选的待确认表不应继续阻塞已选表进入匹配配置", () => {
    expect(
      canContinueFromSmartRecognition(
        [
          recognizedTable({ tableIndex: 0 }),
          recognizedTable({ tableIndex: 1, decision: "NeedConfirm" })
        ],
        [0]
      )
    ).toBe(true);
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

  it("高级模式从表格配置页返回时应回到智能上传页，避免高级步骤条与上传内容错位", () => {
    expect(
      getSmartFillPrevStepState({
        advancedMode: true,
        currentStep: 1
      })
    ).toEqual({
      advancedMode: false,
      currentStep: 0
    });
  });
});
