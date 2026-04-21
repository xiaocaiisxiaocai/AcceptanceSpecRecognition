import test from "node:test";
import assert from "node:assert/strict";

import {
  applyExcelBatchTableRowFieldChange,
  normalizeExcelBatchTableRows
} from "../src/views/smart-fill/components/batchTableConfig.helpers.ts";

test("批量表格配置修改表头起始行后应重算数据起始行", () => {
  const next = applyExcelBatchTableRowFieldChange(
    {
      tableInfo: {
        usedRangeStartRow: 3
      },
      headerRowStart: 3,
      headerRowCount: 1,
      dataStartRow: 9
    },
    "headerRowStart",
    5
  );

  assert.equal(next.headerRowStart, 5);
  assert.equal(next.headerRowCount, 1);
  assert.equal(next.dataStartRow, 6);
});

test("批量表格配置修改表头行数后应重算数据起始行", () => {
  const next = applyExcelBatchTableRowFieldChange(
    {
      tableInfo: {
        usedRangeStartRow: 2
      },
      headerRowStart: 2,
      headerRowCount: 1,
      dataStartRow: 8
    },
    "headerRowCount",
    3
  );

  assert.equal(next.headerRowStart, 2);
  assert.equal(next.headerRowCount, 3);
  assert.equal(next.dataStartRow, 5);
});

test("批量表格配置直接修改数据起始行时仍应受表头末行约束", () => {
  const next = applyExcelBatchTableRowFieldChange(
    {
      tableInfo: {
        usedRangeStartRow: 4
      },
      headerRowStart: 4,
      headerRowCount: 2,
      dataStartRow: 6
    },
    "dataStartRow",
    3
  );

  assert.equal(next.dataStartRow, 6);
});

test("批量表格行范围归一化保持当前最小可用值", () => {
  const normalized = normalizeExcelBatchTableRows({
    tableInfo: {
      usedRangeStartRow: 4
    },
    headerRowStart: 2,
    headerRowCount: 1,
    dataStartRow: 4
  });

  assert.equal(normalized.headerRowStart, 4);
  assert.equal(normalized.dataStartRow, 5);
});
