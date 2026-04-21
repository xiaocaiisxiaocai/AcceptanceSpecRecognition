import test from "node:test";
import assert from "node:assert/strict";

import {
  applyExcelMappingRowFieldChange,
  createDefaultExcelMapping,
  normalizeExcelMappingByTable
} from "../src/views/data-import/dataImport.helpers.ts";

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
