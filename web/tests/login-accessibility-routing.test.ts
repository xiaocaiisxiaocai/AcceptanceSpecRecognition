import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = (() => {
  const cwd = process.cwd();
  return existsSync(resolve(cwd, "web/package.json"))
    ? cwd
    : resolve(cwd, "..");
})();
const readProjectFile = (path: string) =>
  readFileSync(resolve(repositoryRoot, path), "utf8");

test("登录页应声明中文、允许缩放并提供语义化表单控件", () => {
  const html = readProjectFile("web/index.html");
  const login = readProjectFile("web/src/views/login/index.vue");

  assert.match(html, /<html lang="zh-CN">/);
  assert.doesNotMatch(html, /user-scalable=0|maximum-scale=1/);
  assert.match(login, /for="login-username"/);
  assert.match(login, /for="login-password"/);
  assert.match(login, /native-type="submit"/);
  assert.match(login, /:aria-label="dataTheme/);
});

test("登录页动效应尊重 reduced-motion", () => {
  const html = readProjectFile("web/index.html");
  const loginCss = readProjectFile("web/src/style/login.css");
  const motion = readProjectFile("web/src/views/login/utils/motion.ts");

  assert.match(html, /prefers-reduced-motion: reduce/);
  assert.match(loginCss, /prefers-reduced-motion: reduce/);
  assert.match(motion, /useMediaQuery\("\(prefers-reduced-motion: reduce\)"\)/);
});

test("路由滚动应始终返回位置且登录判断使用不带查询参数的 path", () => {
  const router = readProjectFile("web/src/router/index.ts");

  assert.match(router, /if \(savedPosition\) return savedPosition;/);
  assert.match(router, /return \{ left: 0, top: 0 \};/);
  assert.match(router, /whiteList\.includes\(to\.path\)/);
  assert.doesNotMatch(router, /whiteList\.includes\(to\.fullPath\)/);
});

test("权限过滤后只剩一个子菜单时应直接导航到该子页面", () => {
  const sidebarItem = readProjectFile(
    "web/src/layout/components/lay-sidebar/components/SidebarItem.vue"
  );

  assert.match(sidebarItem, /<SidebarLinkItem[\s\S]*?:to="onlyOneChild"/);
  assert.doesNotMatch(sidebarItem, /<SidebarLinkItem[\s\S]*?:to="item"/);
});
