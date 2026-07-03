import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync } from "node:fs";
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

const readProjectFilesByPrefix = (directoryPath: string, prefix: string) =>
  readdirSync(resolve(repositoryRoot, directoryPath))
    .filter(file => file.startsWith(prefix) && file.endsWith(".cs"))
    .map(file => readProjectFile(`${directoryPath}/${file}`))
    .join("\n");

test("smart-fill 匹配 API 应暴露 previewRequestId 与进度查询接口", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");

  assert.match(matchingApiSource, /previewRequestId\?: string;/);
  assert.match(matchingApiSource, /export interface BatchPreviewProgressResponse \{/);
  assert.match(matchingApiSource, /export const getBatchPreviewProgress = \(/);
});

test("smart-fill 批量预览接口应显式关闭 axios 默认超时，避免慢模型在 10 秒默认超时后被前端取消", () => {
  const matchingApiSource = readProjectFile("web/src/api/matching.ts");
  const batchPreviewMatchBlock =
    matchingApiSource.match(
      /export const batchPreviewMatch = \([\s\S]*?\n\};/
    )?.[0] ?? "";

  assert.match(batchPreviewMatchBlock, /export const batchPreviewMatch = \(/);
  assert.match(batchPreviewMatchBlock, /timeout:\s*0/);
  assert.doesNotMatch(batchPreviewMatchBlock, /timeout:\s*300000/);
});

test("smart-fill 页面应维护真实进度状态并轮询 preview 进度", () => {
  const smartFillPageSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const progressSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewProgress.ts"
  );

  assert.match(progressSource, /const previewProgress = ref</);
  assert.match(progressSource, /const previewElapsedSeconds = ref\(0\);/);
  assert.match(progressSource, /const startPreviewProgressPolling = \(/);
  assert.match(progressSource, /const stopPreviewProgressPolling = \(\) => \{/);
  assert.match(progressSource, /getBatchPreviewProgress\(/);
  const previewStepSource = readProjectFile(
    "web/src/views/smart-fill/components/SmartFillPreviewStep.vue"
  );
  assert.match(previewStepSource, /class="matching-loading"/);
  assert.match(previewStepSource, /\{\{\s*previewProgressStageText\s*\}\}/);
  assert.match(previewStepSource, /\{\{\s*previewElapsedSeconds\s*\}\}/);
});

test("smart-fill 进度轮询遇到失效 requestId 时应停止继续轮询", () => {
  const progressSource = readProjectFile(
    "web/src/views/smart-fill/composables/useSmartFillPreviewProgress.ts"
  );

  assert.match(progressSource, /if \(axiosError\?\.response\?\.status === 404\) \{/);
  assert.match(progressSource, /if \(!isLoading\(\)\) \{/);
  assert.match(progressSource, /stopPreviewProgressPolling\(\);/);
});

test("batch-preview 控制器与应用服务应透传请求取消令牌，避免刷新页面后后端继续跑", () => {
  const controllerSource = readProjectFile(
    "src/AcceptanceSpecSystem.Api/Controllers/MatchingPreviewController.cs"
  );
  const appServiceSource = readProjectFile(
    "src/AcceptanceSpecSystem.Api/Services/MatchingPreviewAppService.cs"
  );

  assert.match(
    controllerSource,
    /BatchPreviewAsync\(User,\s*request,\s*HttpContext\.RequestAborted\)/
  );
  assert.match(
    appServiceSource,
    /BatchPreviewAsync\(\s*ClaimsPrincipal user,\s*BatchPreviewRequest request,\s*CancellationToken cancellationToken = default\)/
  );
  assert.match(
    appServiceSource,
    /_matchingService\.BatchMatchAsync\(\s*allSources,\s*processedCandidates,\s*config,\s*CreateBatchMatchProgressReporter\(previewRequestId\),\s*cancellationToken\)/
  );
});

test("匹配服务应在批量预览链路中把取消令牌下传到 Embedding、并行处理与 LLM 裁决", () => {
  const matchingInterfaceSource = readProjectFile(
    "src/AcceptanceSpecSystem.Core/Matching/Interfaces/IMatchingService.cs"
  );
  const matchingServiceSource = readProjectFilesByPrefix(
    "src/AcceptanceSpecSystem.Core/Matching/Services",
    "SemanticKernelMatchingService"
  );

  assert.match(
    matchingInterfaceSource,
    /BatchMatchAsync\(\s*IEnumerable<MatchSource> sources,\s*IEnumerable<MatchCandidate> candidates,\s*MatchingConfig\? config = null,\s*IProgress<BatchMatchProgress>\? progress = null,\s*CancellationToken cancellationToken = default\)/
  );
  assert.match(
    matchingServiceSource,
    /cancellationToken/
  );
  assert.match(
    matchingServiceSource,
    /GenerateEmbeddingsAsync\(\s*pendingSourceIndices\.Select\(index => GetSourceEmbeddingText\(sourceList\[index\], config\)\),\s*config\.EmbeddingServiceId,\s*cancellationToken\)/
  );
  assert.match(
    matchingServiceSource,
    /RerankAsync\([\s\S]*cancellationToken\)/
  );
  assert.match(
    matchingServiceSource,
    /AdjudicateAsync\([\s\S]*cancellationToken\)/
  );
});

test("批量预览 Tabs 应只渲染当前激活表，避免一次性挂载全部表格", () => {
  const previewTabsSource = readProjectFile(
    "web/src/views/smart-fill/components/BatchPreviewTabs.vue"
  );

  assert.match(previewTabsSource, /const activeTableResult = computed\(/);
  assert.match(previewTabsSource, /v-if="activeTableResult"/);
  assert.match(previewTabsSource, /:items="activeTableResult\.items"/);
  assert.doesNotMatch(previewTabsSource, /:items="tableResult\.items"/);
  assert.doesNotMatch(previewTabsSource, /emit\('select', tableResult\.tableIndex/);
});
