import test from "node:test";
import assert from "node:assert/strict";

import {
  differenceColumnDefs,
  formatDifferenceValue,
  formatScorePercent,
  getDifferenceMatchTypeLabel,
  getDifferenceMatchTypeTagType,
  hasAiDifferenceMeta,
  isDifferenceColumnChanged
} from "../src/views/data-import/dataImport.difference-formatters.ts";
import type { ImportPendingDifferenceWithTable } from "../src/views/data-import/dataImport.types.ts";

const createDifference = (
  overrides: Partial<ImportPendingDifferenceWithTable> = {}
): ImportPendingDifferenceWithTable => ({
  tableIndex: 0,
  key: "0:1",
  rowIndex: 1,
  existingProject: "项目A",
  incomingProject: "项目A",
  existingSpecification: "规格A",
  incomingSpecification: "规格A",
  existingSpecId: 1,
  existingAcceptance: "旧验收",
  incomingAcceptance: "新验收",
  existingRemark: "",
  incomingRemark: "备注",
  matchType: "conflict",
  ...overrides
});

test("差异弹窗格式化应统一空值、分数和匹配类型展示", () => {
  assert.equal(formatDifferenceValue("  abc  "), "abc");
  assert.equal(formatDifferenceValue("   "), "-");
  assert.equal(formatDifferenceValue(null), "-");
  assert.equal(formatScorePercent(0.876), "87.6%");
  assert.equal(formatScorePercent(undefined), "-");
  assert.equal(formatScorePercent(Number.NaN), "-");
  assert.equal(getDifferenceMatchTypeLabel("exact"), "完全重复");
  assert.equal(getDifferenceMatchTypeLabel("semantic"), "AI 疑似重复");
  assert.equal(getDifferenceMatchTypeLabel("conflict"), "同项目同规格");
  assert.equal(getDifferenceMatchTypeTagType("exact"), "danger");
  assert.equal(getDifferenceMatchTypeTagType("semantic"), "success");
  assert.equal(getDifferenceMatchTypeTagType("conflict"), "warning");
});

test("差异弹窗字段定义应覆盖项目、规格、验收和备注，并能判断字段变化", () => {
  const item = createDifference();
  assert.deepEqual(
    differenceColumnDefs.map(column => column.key),
    ["project", "specification", "acceptance", "remark"]
  );

  const acceptanceColumn = differenceColumnDefs.find(column => column.key === "acceptance");
  const projectColumn = differenceColumnDefs.find(column => column.key === "project");
  assert.ok(acceptanceColumn);
  assert.ok(projectColumn);
  assert.equal(isDifferenceColumnChanged(item, acceptanceColumn), true);
  assert.equal(isDifferenceColumnChanged(item, projectColumn), false);
});

test("差异弹窗只在 AI 疑似重复且带复核信息时展示 AI 元信息", () => {
  assert.equal(hasAiDifferenceMeta(createDifference({ matchType: "semantic", embeddingScore: 0.9 })), true);
  assert.equal(hasAiDifferenceMeta(createDifference({ matchType: "semantic", reviewReason: "相似" })), true);
  assert.equal(hasAiDifferenceMeta(createDifference({ matchType: "semantic" })), false);
  assert.equal(hasAiDifferenceMeta(createDifference({ matchType: "conflict", embeddingScore: 0.9 })), false);
});
