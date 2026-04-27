import test from "node:test";
import assert from "node:assert/strict";

import {
  parseExecutionHistoryDateTime,
  formatExecutionHistoryDateTime
} from "../src/views/other/execution-history/executionHistory.formatters.ts";

test("执行记录后端无时区时间应按 UTC 解析", () => {
  const parsed = parseExecutionHistoryDateTime("2026-04-27T01:49:27");

  assert.equal(parsed?.toISOString(), "2026-04-27T01:49:27.000Z");
  assert.equal(parsed?.getTime(), Date.UTC(2026, 3, 27, 1, 49, 27));
});

test("执行记录后端无时区空格格式也应按 UTC 解析", () => {
  const parsed = parseExecutionHistoryDateTime("2026-04-27 01:49:27");

  assert.equal(parsed?.toISOString(), "2026-04-27T01:49:27.000Z");
});

test("执行记录已有时区标记时保持原始时区语义", () => {
  const parsed = parseExecutionHistoryDateTime("2026-04-27T01:49:27+08:00");

  assert.equal(parsed?.toISOString(), "2026-04-26T17:49:27.000Z");
});

test("执行记录空时间或非法时间显示占位符", () => {
  assert.equal(formatExecutionHistoryDateTime(), "-");
  assert.equal(formatExecutionHistoryDateTime("not-a-date"), "-");
});
