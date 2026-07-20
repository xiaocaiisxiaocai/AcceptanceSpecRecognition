import { describe, expect, it } from "vitest";
import {
  buildSkippedPreviewColumns,
  getSkippedPreviewHeaderGroupLabel,
  mergeSkippedPreviewCellValues
} from "./dataImport.helpers";

describe("跳过详情表头合并", () => {
  it.each([
    ["項目 / 項目", "項目"],
    ["細項 / 細項", "細項"],
    ["規格 / 規格 / 規格", "規格"],
    ["廠商確認 / OK/NG", "廠商確認"],
    ["廠商確認 / Remark", "廠商確認"]
  ])("提取多行表头的第一层 %s", (source, expected) => {
    expect(getSkippedPreviewHeaderGroupLabel(source)).toBe(expected);
  });

  it("将相邻的同组原始列合并为一个展示列", () => {
    expect(
      buildSkippedPreviewColumns(
        [
          "項目 / 項目",
          "細項 / 細項",
          "規格 / 規格",
          "規格 / 規格",
          "廠商確認 / OK/NG",
          "廠商確認 / Remark",
          "廠內定稿 / Owner"
        ],
        7
      )
    ).toEqual([
      { indexes: [0], label: "項目" },
      { indexes: [1], label: "細項" },
      { indexes: [2, 3], label: "規格" },
      { indexes: [4, 5], label: "廠商確認" },
      { indexes: [6], label: "廠內定稿" }
    ]);
  });

  it("合并同组内容时去空和去重", () => {
    expect(
      mergeSkippedPreviewCellValues(
        ["项目", "说明:", "说明:", "", "OK", "备注"],
        [1, 2, 3, 4, 5]
      )
    ).toBe("说明:；OK；备注");
  });
});
