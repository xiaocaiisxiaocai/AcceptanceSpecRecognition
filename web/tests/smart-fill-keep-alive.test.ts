import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

const statefulWorkflowPages = [
  {
    name: "ImportData",
    route: "src/router/modules/data-import.ts",
    page: "src/views/data-import/index.vue"
  },
  {
    name: "FillData",
    route: "src/router/modules/smart-fill.ts",
    page: "src/views/smart-fill/index.vue"
  },
  {
    name: "BatchReplyPage",
    route: "src/router/modules/batch-reply.ts",
    page: "src/views/batch-reply/index.vue"
  },
  {
    name: "FileComparePage",
    route: "src/router/modules/file-compare.ts",
    page: "src/views/file-compare/index.vue"
  }
];

test("强状态流程页应启用 keep-alive 并让组件名匹配子路由名", () => {
  for (const item of statefulWorkflowPages) {
    const routeSource = readProjectFile(item.route);
    const pageSource = readProjectFile(item.page);

    assert.match(routeSource, new RegExp(`name:\\s*"${item.name}"`));
    assert.match(routeSource, /keepAlive:\s*true/);
    assert.match(pageSource, new RegExp(`name:\\s*"${item.name}"`));
  }
});

test("智能填充进度快照 404 时应停止轮询但不取消主匹配", () => {
  const pageSource = readProjectFile("src/views/smart-fill/index.vue");

  assert.match(pageSource, /error\?\.response\?\.status === 404/);
  assert.match(pageSource, /stopPreviewProgressPolling\(\);\s*return;/);
  assert.doesNotMatch(
    pageSource,
    /error\?\.response\?\.status === 404[\s\S]{0,120}stopPreviewRequest\(\)/
  );
});
