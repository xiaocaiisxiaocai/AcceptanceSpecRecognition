import { describe, expect, it } from "vitest";
import { createSSRApp, h } from "vue";
import { renderToString } from "@vue/server-renderer";
import type { TableData, TableInfo } from "@/api/document";
import CompareTableGrid from "./CompareTableGrid.vue";

const createTableData = (): TableData => ({
  tableIndex: 0,
  headers: ["项目", "规格", "结果"],
  rows: Array.from({ length: 200 }, (_, rowIndex) => [
    `项目-${rowIndex + 201}`,
    `规格-${rowIndex + 201}`,
    `结果-${rowIndex + 201}`
  ]),
  structuredRows: [],
  totalRows: 100_000,
  columnCount: 3,
  rowOffset: 200,
  columnOffset: 60,
  totalColumns: 120
});

const tableInfo: TableInfo = {
  index: 0,
  name: "Sheet1",
  rowCount: 100_000,
  columnCount: 120,
  isNested: false,
  previewText: "",
  headers: ["项目", "规格", "结果"],
  hasMergedCells: false,
  usedRangeStartRow: 1,
  usedRangeStartColumn: 1
};

describe("CompareTableGrid", () => {
  it("大型逻辑表格只渲染服务端返回的当前窗口", async () => {
    const app = createSSRApp({
      render: () =>
        h(CompareTableGrid, {
          tableIndex: 0,
          fileType: 1,
          tableData: createTableData(),
          tableInfo,
          diffMap: new Map(),
          onlyDiff: false
        })
    });
    app.component("ElEmpty", { render: () => null });

    const html = await renderToString(app);
    expect(html.match(/<tbody[\s\S]*?<\/tbody>/)).toHaveLength(1);
    expect(html.match(/<tr/g)).toHaveLength(201);
    expect(html).toContain("当前窗口：200 行");
    expect(html).toContain(">201<");
    expect(html).toContain(">BI<");
    expect(html).not.toContain("项目-401");
  });
});
