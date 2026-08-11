import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const drawerSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/base-data/specs/components/SpecContentVersionDrawer.vue"
  ),
  "utf8"
);

test("内容版本工作台应直接按 V1 到 Vn 完整展开四个正文项", () => {
  assert.match(drawerSource, /class="version-matrix"/);
  assert.match(drawerSource, /v-for="version in versionDetails"/);
  assert.match(drawerSource, /v-for="field in contentFields"/);
  assert.match(drawerSource, /验收规范/);
  assert.doesNotMatch(drawerSource, /class="version-summary"/);
  assert.doesNotMatch(drawerSource, /class="version-rail"/);
  assert.doesNotMatch(drawerSource, /<el-radio-group/);
  assert.doesNotMatch(drawerSource, /class="version-select"/);
});

test("版本矩阵应高亮相邻版本中发生变化的正文", () => {
  assert.match(drawerSource, /const isFieldChanged =/);
  assert.match(drawerSource, /'is-changed': isFieldChanged/);
  assert.match(drawerSource, /class="change-mark"/);
  assert.doesNotMatch(drawerSource, /getSpecContentVersionDiff/);
});

test("精简界面仍应保留迁移缺口、恢复和失败状态", () => {
  assert.match(drawerSource, /版本记录功能上线前的正文不可追溯/);
  assert.match(drawerSource, /恢复此版本/);
  assert.match(drawerSource, /errorMessage/);
  assert.match(drawerSource, /暂无可用内容版本/);
});
