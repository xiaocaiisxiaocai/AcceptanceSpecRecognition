import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const dashboardSource = readFileSync(
  resolve(process.cwd(), "web/src/views/dashboard/index.vue"),
  "utf8"
);

test("首页应使用聚合统计接口加载周期指标", () => {
  assert.match(dashboardSource, /getDashboardSummary/);
  assert.match(dashboardSource, /periodPreset/);
  assert.match(dashboardSource, /matchingRate/);
  assert.match(dashboardSource, /importedSpecCount/);
});

test("首页周期筛选应支持最近7天、最近30天和自定义", () => {
  assert.match(dashboardSource, /最近7天/);
  assert.match(dashboardSource, /最近30天/);
  assert.match(dashboardSource, /自定义/);
  assert.match(dashboardSource, /type="datetimerange"/);
});
