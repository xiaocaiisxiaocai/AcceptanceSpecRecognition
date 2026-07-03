import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), relativePath), "utf8");

const statefulWorkflowPages = [
  {
    name: "ImportData",
    route: "web/src/router/modules/data-import.ts",
    page: "web/src/views/data-import/index.vue"
  },
  {
    name: "FillData",
    route: "web/src/router/modules/smart-fill.ts",
    page: "web/src/views/smart-fill/index.vue"
  },
  {
    name: "BatchReplyPage",
    route: "web/src/router/modules/batch-reply.ts",
    page: "web/src/views/batch-reply/index.vue"
  },
  {
    name: "FileComparePage",
    route: "web/src/router/modules/file-compare.ts",
    page: "web/src/views/file-compare/index.vue"
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
  const progressSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewProgress.ts"
  );

  assert.match(progressSource, /axiosError\?\.response\?\.status === 404/);
  assert.match(progressSource, /stopPreviewProgressPolling\(\);/);
  assert.doesNotMatch(
    progressSource,
    /axiosError\?\.response\?\.status === 404[\s\S]{0,120}stopPreviewRequest\(\)/
  );
});
