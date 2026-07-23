import { describe, expect, it } from "vitest";
import type { DocumentTemplateRegion } from "@/api/document-templates";
import { getSmartStructureSourceLabel } from "@/views/shared/smart-structure-recognition";
import {
  formatTemplateDataRange,
  formatTemplateHeaderRange,
  getTemplateRegionRanges
} from "./document-template-display";

const region: DocumentTemplateRegion = {
  regionIndex: 0,
  headers: ["项目", "规格", "验收", "备注"],
  headerRowIndex: 7,
  headerRowCount: 1,
  dataStartRowIndex: 8,
  dataEndRowIndex: 111,
  projectColumnIndex: 2,
  specificationColumnIndex: 3,
  acceptanceColumnIndex: 8,
  remarkColumnIndex: 9,
  isSpecificationOnly: false
};

describe("结构模板展示", () => {
  it("将模板索引显示为单列 A1 范围", () => {
    expect(getTemplateRegionRanges(region).map(item => item.value)).toEqual([
      "C9:C112",
      "D9:D112",
      "I9:I112",
      "J9:J112"
    ]);
    expect(formatTemplateHeaderRange(region)).toBe("第 8 行");
    expect(formatTemplateDataRange(region)).toBe("第 9–112 行");
  });

  it("兼容仅规格和开放结束行", () => {
    expect(
      getTemplateRegionRanges({
        ...region,
        isSpecificationOnly: true,
        projectColumnIndex: null,
        dataEndRowIndex: null
      }).map(item => item.value)
    ).toEqual(["仅规格表", "D9:D末行", "I9:I末行", "J9:J末行"]);
  });

  it("将内部识别来源转换为中文", () => {
    expect(getSmartStructureSourceLabel("Template")).toBe("历史模板");
    expect(getSmartStructureSourceLabel("RuleBased")).toBe("规则识别");
    expect(getSmartStructureSourceLabel("CustomSource")).toBe("CustomSource");
  });
});
