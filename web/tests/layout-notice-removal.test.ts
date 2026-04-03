import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const navbarSource = readFileSync(
  resolve(process.cwd(), "src/layout/components/lay-navbar/index.vue"),
  "utf8"
);

const navHorizontalSource = readFileSync(
  resolve(process.cwd(), "src/layout/components/lay-sidebar/NavHorizontal.vue"),
  "utf8"
);

const navMixSource = readFileSync(
  resolve(process.cwd(), "src/layout/components/lay-sidebar/NavMix.vue"),
  "utf8"
);

const sidebarStyleSource = readFileSync(
  resolve(process.cwd(), "src/style/sidebar.scss"),
  "utf8"
);

test("顶部布局不应再渲染通知组件入口", () => {
  for (const source of [navbarSource, navHorizontalSource, navMixSource]) {
    assert.doesNotMatch(source, /LayNotice/);
    assert.doesNotMatch(source, /header-notice/);
    assert.doesNotMatch(source, /消息通知/);
  }
});

test("侧边栏样式不应保留通知徽标样式", () => {
  assert.doesNotMatch(sidebarStyleSource, /dropdown-badge/);
  assert.doesNotMatch(sidebarStyleSource, /消息通知/);
});

test("通知组件目录应被移除", () => {
  assert.equal(
    existsSync(resolve(process.cwd(), "src/layout/components/lay-notice")),
    false
  );
});
