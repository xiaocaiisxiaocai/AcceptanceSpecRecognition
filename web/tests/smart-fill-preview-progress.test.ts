import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = (() => {
  const cwd = process.cwd();
  if (existsSync(resolve(cwd, "web/package.json"))) {
    return cwd;
  }

  return resolve(cwd, "..");
})();

const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(repositoryRoot, relativePath), "utf8");

test("smart-fill 匹配 API 应暴露 previewRequestId 与进度查询接口", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.match(matchingApiSource, /previewRequestId\?: string;/);
  assert.match(matchingApiSource, /export interface BatchPreviewProgressResponse \{/);
  assert.match(matchingApiSource, /export const getBatchPreviewProgress = \(/);
});

test("smart-fill 页面应维护真实进度状态并轮询 preview 进度", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");

  assert.match(smartFillPageSource, /const previewProgress = ref</);
  assert.match(smartFillPageSource, /const previewElapsedSeconds = ref\(0\);/);
  assert.match(smartFillPageSource, /const startPreviewProgressPolling = \(/);
  assert.match(smartFillPageSource, /const stopPreviewProgressPolling = \(\) => \{/);
  assert.match(smartFillPageSource, /getBatchPreviewProgress\(/);
  assert.match(smartFillPageSource, /class="preview-progress-panel"/);
  assert.match(smartFillPageSource, /\{\{\s*previewProgressStageText\s*\}\}/);
  assert.match(smartFillPageSource, /\{\{\s*previewElapsedSeconds\s*\}\}/);
});

test("批量预览 Tabs 应只渲染当前激活表，避免一次性挂载全部表格", () => {
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );

  assert.match(previewTabsSource, /const activeTableResult = computed\(\(\) =>/);
  assert.match(previewTabsSource, /v-if="activeTableResult"/);
  assert.match(previewTabsSource, /:items="activeTableResult\.items"/);
  assert.doesNotMatch(previewTabsSource, /:items="tableResult\.items"/);
  assert.doesNotMatch(previewTabsSource, /emit\('select', tableResult\.tableIndex/);
});
