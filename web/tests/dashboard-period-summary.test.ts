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
  assert.match(dashboardSource, /dashboard-period-range/);
  assert.match(
    dashboardSource,
    /formatDateTime\(currentSummary\.periodStart\)/
  );
  assert.match(dashboardSource, /formatDateTime\(currentSummary\.periodEnd\)/);
  assert.doesNotMatch(dashboardSource, /class="page-subtitle"/);
});

test("首页周期筛选应支持最近7天、最近30天和自定义", () => {
  assert.match(dashboardSource, /最近7天/);
  assert.match(dashboardSource, /最近30天/);
  assert.match(dashboardSource, /自定义/);
  assert.match(dashboardSource, /type="datetimerange"/);
});

test("首页应移除中部图表区并保留最近执行记录", () => {
  assert.doesNotMatch(dashboardSource, /匹配采用分布/);
  assert.doesNotMatch(dashboardSource, /周期业务量/);
  assert.doesNotMatch(dashboardSource, /dashboard-chart-grid/);
  assert.doesNotMatch(dashboardSource, /chart-panel/);
  assert.doesNotMatch(dashboardSource, /height="100%"/);
  assert.doesNotMatch(dashboardSource, /height:\s*260px/);
  assert.match(dashboardSource, /currentSummary\.value\.recentExecutions/);
  assert.doesNotMatch(dashboardSource, /getExecutionHistoryList/);
  assert.match(dashboardSource, /最近执行/);
});

test("管理员可筛选部门且普通用户不展示部门筛选", () => {
  assert.match(dashboardSource, /userStore\.roleCode === "admin"/);
  assert.match(dashboardSource, /v-if="isAdmin"/);
  assert.match(dashboardSource, /selectedOrgUnitId/);
  assert.match(dashboardSource, /placeholder="公司总体"/);
});
