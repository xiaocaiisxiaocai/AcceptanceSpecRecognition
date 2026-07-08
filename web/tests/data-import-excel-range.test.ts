import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  applyExcelMappingRowFieldChange,
  buildExcelColumnOptions,
  createDefaultExcelMapping,
  normalizeExcelMappingByTable
} from "../src/views/data-import/dataImport.helpers.ts";

const excelColumnMappingSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/components/ExcelColumnMapping.vue"),
  "utf8"
);

test("Excel 默认映射应包含已用区域末行作为数据结束行", () => {
  const mapping = createDefaultExcelMapping({
    index: 0,
    name: "Sheet1",
    rowCount: 6,
    columnCount: 4,
    isNested: false,
    previewText: "",
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 3,
    usedRangeStartColumn: 1
  });

  assert.equal(mapping.headerRowStart, 3);
  assert.equal(mapping.dataStartRow, 4);
  assert.equal(mapping.dataEndRow, 8);
});

test("Excel 行范围归一化应将数据结束行限制在数据起始行和已用区域末行之间", () => {
  const tableInfo = {
    index: 0,
    name: "Sheet1",
    rowCount: 5,
    columnCount: 4,
    isNested: false,
    previewText: "",
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 2,
    usedRangeStartColumn: 1
  };

  const clampedToUsedRange = normalizeExcelMappingByTable(tableInfo, {
    headerRowStart: 2,
    headerRowCount: 1,
    dataStartRow: 4,
    dataEndRow: 99
  });

  assert.equal(clampedToUsedRange.dataEndRow, 6);

  const raisedToDataStart = normalizeExcelMappingByTable(tableInfo, {
    headerRowStart: 2,
    headerRowCount: 1,
    dataStartRow: 5,
    dataEndRow: 4
  });

  assert.equal(raisedToDataStart.dataEndRow, 5);
});

test("修改表头起始行后应重算数据起始行为表头末行", () => {
  const tableInfo = {
    index: 0,
    name: "Sheet1",
    rowCount: 8,
    columnCount: 4,
    isNested: false,
    previewText: "",
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 3,
    usedRangeStartColumn: 1
  };

  const next = applyExcelMappingRowFieldChange(
    tableInfo,
    {
      headerRowStart: 3,
      headerRowCount: 1,
      dataStartRow: 9,
      dataEndRow: 10
    },
    "headerRowStart",
    5
  );

  assert.equal(next.headerRowStart, 5);
  assert.equal(next.dataStartRow, 6);
  assert.equal(next.dataEndRow, 10);
});

test("修改表头行数后应重算数据起始行为表头末行", () => {
  const tableInfo = {
    index: 0,
    name: "Sheet1",
    rowCount: 8,
    columnCount: 4,
    isNested: false,
    previewText: "",
    headers: [],
    hasMergedCells: false,
    usedRangeStartRow: 2,
    usedRangeStartColumn: 1
  };

  const next = applyExcelMappingRowFieldChange(
    tableInfo,
    {
      headerRowStart: 2,
      headerRowCount: 1,
      dataStartRow: 7,
      dataEndRow: 8
    },
    "headerRowCount",
    3
  );

  assert.equal(next.headerRowCount, 3);
  assert.equal(next.dataStartRow, 5);
  assert.equal(next.dataEndRow, 8);
});

test("Excel 列选项应显示本地列序号、Excel 字母和字段行文本", () => {
  const options = buildExcelColumnOptions(
    {
      index: 0,
      name: "Sheet1",
      rowCount: 10,
      columnCount: 4,
      isNested: false,
      previewText: "",
      headers: [],
      hasMergedCells: false,
      usedRangeStartRow: 1,
      usedRangeStartColumn: 3
    },
    {
      tableIndex: 0,
      headers: ["标题", "标题", "标题", "标题"],
      rows: [
        ["说明", "说明", "说明", "说明"],
        ["序号", "项目", "规格要求", "备注"]
      ],
      totalRows: 3,
      columnCount: 4
    }
  );

  assert.equal(options[1].value, 4);
  assert.equal(options[1].label, "第 2 列（D）标题");
  assert.equal(options[2].label, "第 3 列（E）标题");
});

test("Excel 映射组件应复用后端识别结果而不是前端词表识别", () => {
  const helperSource = readFileSync(
    resolve(process.cwd(), "web/src/views/data-import/dataImport.helpers.ts"),
    "utf8"
  );

  assert.doesNotMatch(helperSource, /scoreFieldHeader/);
  assert.doesNotMatch(helperSource, /detectExcelFieldMappingFromPreview/);
  assert.match(excelColumnMappingSource, /detectedMapping/);
  assert.match(excelColumnMappingSource, /重置为识别结果/);
  assert.doesNotMatch(excelColumnMappingSource, /detectExcelFieldMappingFromPreview/);
});

test("Excel 映射组件的列映射应使用字段下拉选项", () => {
  assert.match(excelColumnMappingSource, /buildExcelColumnOptions/);
  assert.match(excelColumnMappingSource, /v-for="opt in columnOptions"/);
});
