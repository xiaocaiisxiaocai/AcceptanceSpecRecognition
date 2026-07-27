import test from "node:test";
import assert from "node:assert/strict";

import {
  buildBatchReplyPreviewFingerprint,
  createTargetPreviewLoaderResolver,
  prunePreviewResultsForConfigChange
} from "../src/views/batch-reply/batch-reply-preview-state.ts";

const config = (acceptanceColumnIndex: number) => ({
  tableIndex: 0,
  sourceTableIndex: 0,
  projectColumnIndex: 0,
  specificationColumnIndex: 1,
  acceptanceColumnIndex,
  remarkColumnIndex: 3,
  headerRowStart: 1,
  headerRowCount: 1,
  dataStartRow: 2,
  filterEmptySourceRows: true,
  duplicateResolutions: [],
  selected: true
});

test("配置指纹变化后旧响应不能恢复 canApply", () => {
  const before = buildBatchReplyPreviewFingerprint("s1", "t1", config(1));
  const after = buildBatchReplyPreviewFingerprint("s1", "t1", config(2));

  assert.notEqual(before, after);
  assert.notEqual(
    before,
    buildBatchReplyPreviewFingerprint("s2", "t1", config(1))
  );
  assert.notEqual(
    before,
    buildBatchReplyPreviewFingerprint("s1", "t2", config(1))
  );
});

test("相同 targetId 的预览加载器应复用同一个函数引用", () => {
  const resolver = createTargetPreviewLoaderResolver(targetId => async () => ({
    tableIndex: 0,
    headers: [targetId],
    rows: [],
    totalRows: 0,
    columnCount: 1
  }));

  const loaderA1 = resolver("target-a");
  const loaderA2 = resolver("target-a");
  const loaderB = resolver("target-b");

  assert.equal(loaderA1, loaderA2);
  assert.notEqual(loaderA1, loaderB);
});

test("字段设置变更时只清理被修改表格的预览结果", () => {
  const previousConfigs = [
    {
      tableIndex: 0,
      sourceTableIndex: 0,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowStart: 1,
      headerRowCount: 1,
      dataStartRow: 2,
      filterEmptySourceRows: true,
      duplicateResolutions: [],
      selected: true
    },
    {
      tableIndex: 1,
      sourceTableIndex: 1,
      projectColumnIndex: 0,
      specificationColumnIndex: 1,
      acceptanceColumnIndex: 2,
      remarkColumnIndex: 3,
      headerRowStart: 1,
      headerRowCount: 1,
      dataStartRow: 2,
      filterEmptySourceRows: true,
      duplicateResolutions: [],
      selected: true
    }
  ];

  const nextConfigs = [
    {
      ...previousConfigs[0],
      acceptanceColumnIndex: 4
    },
    previousConfigs[1]
  ];

  const nextPreviewResults = prunePreviewResultsForConfigChange(
    {
      0: { canApply: true },
      1: { canApply: true }
    },
    previousConfigs,
    nextConfigs
  );

  assert.deepEqual(nextPreviewResults, {
    1: { canApply: true }
  });
});
