import { describe, expect, it } from "vitest";
import {
  buildSkippedPreviewColumns,
  buildSkippedRowsGroups,
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

  it("跳过明细只按映射输出四个业务字段", () => {
    const [group] = buildSkippedRowsGroups(
      [
        {
          tableIndex: 0,
          rowIndex: 8,
          message: "数据库中已存在相同内容",
          rowValues: ["分类", "项目值", "规格值", "验收值", "备注值", "其它"]
        }
      ],
      [
        {
          tableIndex: 0,
          excelMapping: {
            projectColumn: 2,
            specificationColumn: 3,
            acceptanceColumn: 4,
            remarkColumn: 5,
            headerRowStart: 1,
            headerRowCount: 1,
            dataStartRow: 2,
            dataEndRow: 8
          },
          previewData: null
        }
      ]
    );

    expect(group.columns).toEqual([
      { indexes: [1], label: "项目" },
      { indexes: [2], label: "规格" },
      { indexes: [3], label: "验收" },
      { indexes: [4], label: "备注" }
    ]);
  });
});
