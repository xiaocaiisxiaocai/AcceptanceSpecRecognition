import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

import {
  handleTopMenuCandidate,
  resolveTopMenuFromWholeMenus
} from "../src/router/top-menu.ts";

test("空菜单时返回 undefined，而不是抛异常", () => {
  assert.equal(resolveTopMenuFromWholeMenus([]), undefined);
  assert.equal(resolveTopMenuFromWholeMenus(undefined), undefined);
});

test("存在 redirect 时优先返回被重定向的子菜单", () => {
  const result = handleTopMenuCandidate({
    path: "/config",
    redirect: "/config/prompt-templates",
    children: [
      {
        path: "/config/ai-services"
      },
      {
        path: "/config/prompt-templates"
      }
    ]
  });

  assert.deepEqual(result, {
    path: "/config/prompt-templates"
  });
});

test("只有单个子菜单时保留当前菜单作为跳转目标", () => {
  const result = handleTopMenuCandidate({
    path: "/dashboard",
    children: [
      {
        path: "/welcome"
      }
    ]
  });

  assert.deepEqual(result, {
    path: "/dashboard",
    children: [
      {
        path: "/welcome"
      }
    ]
  });
});

test("欢迎页不应再渲染独立跳转页面", () => {
  const homeRouteSource = readFileSync(
    resolve(process.cwd(), "web/src/router/modules/home.ts"),
    "utf8"
  );

  assert.doesNotMatch(homeRouteSource, /path: "\/welcome"/);
  assert.doesNotMatch(homeRouteSource, /name: "Welcome"/);
});
