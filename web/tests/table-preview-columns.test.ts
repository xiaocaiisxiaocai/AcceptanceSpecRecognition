import test from "node:test";
import assert from "node:assert/strict";

import {
  normalizePreviewHeaders,
  resolvePreviewColumnCount
} from "../src/views/data-import/components/table-preview-columns.ts";

test("当列总数大于表头数量时应补齐占位表头", () => {
  const headers = normalizePreviewHeaders({
    headers: ["说明", "带*号字段为必填项", "列3"],
    columnCount: 4
  });

  assert.deepEqual(headers, ["说明", "带*号字段为必填项", "列3", ""]);
});

test("当数据行列数比表头更多时应按最长列数渲染", () => {
  const columnCount = resolvePreviewColumnCount({
    headers: ["项目", "规格"],
    rows: [["A", "B", "C"]],
    columnCount: 2
  });

  assert.equal(columnCount, 3);
});
