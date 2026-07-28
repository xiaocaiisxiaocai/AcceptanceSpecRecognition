import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

test("SmartConfiguration 配置应显式暴露列语义召回预算", () => {
  const appsettings = JSON.parse(
    readProjectFile("src/AcceptanceSpecSystem.Api/appsettings.json")
  );

  assert.equal(
    appsettings.SmartConfiguration.MaxLlmCallsPerRecognizeDocument,
    5
  );
  assert.equal(
    appsettings.SmartConfiguration.MaxColumnSemanticRecallCallsPerDocument,
    5
  );
});

test("登录接口文档应使用当前 /login 路径", () => {
  const claude = readProjectFile("CLAUDE.md");
  const diagrams = readProjectFile("docs/diagrams.md");

  assert.match(claude, /POST \/login\s+登录/);
  assert.doesNotMatch(claude, /POST \/api\/auth\/login/);
  assert.doesNotMatch(diagrams, /POST \/api\/auth\/login/);
});

test("智能识别读取表格列表后应回写上传文件表格状态", () => {
  const source = readProjectFile(
    "web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts"
  );

  assert.match(source, /tableCountReady:\s*true/);
  assert.match(source, /tableCount:\s*tables\.length/);
});

test("两个智能结构识别入口应仅在 LLM 与 Embedding 均可用时自动启用", () => {
  const dataImportSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportPage.ts"
  );
  const smartFillSource = readProjectFile("web/src/views/smart-fill/index.vue");
  const dataImportRecognitionSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts"
  );
  const controlSource = readProjectFile(
    "web/src/views/shared/SmartStructureAiAssistControl.vue"
  );

  assert.match(dataImportSource, /enableStructureLlmAssistance = ref\(true\)/);
  assert.match(smartFillSource, /enableStructureLlmAssistance = ref\(true\)/);
  assert.match(
    controlSource,
    /loadRuntimeAiSelectionsSettled\(\s*\["embedding",\s*"llm"\]/
  );
  assert.match(
    controlSource,
    /resolveAiAssistSelectionState\(\s*llmSelection\.value,\s*embeddingSelection\.value\s*\)/
  );
  assert.match(controlSource, /emit\("update:enabled", next\.enabled\)/);
  assert.match(controlSource, /emit\("update:serviceId", next\.serviceId\)/);
  assert.match(controlSource, /createAiSelectionRetryController/);
  assert.match(
    controlSource,
    /retryStatuses:\s*\["checking",\s*"unavailable"\]/
  );
  assert.match(
    controlSource,
    /delayMsByStatus:\s*\{\s*unavailable:\s*5000\s*\}/
  );
  assert.doesNotMatch(
    controlSource,
    /v-if="canConfigureAiServices && selection\.status === 'checking'"/
  );
  assert.doesNotMatch(controlSource, /services\.value\[0\]/);
  assert.doesNotMatch(controlSource, /getAiServiceList/);
  assert.doesNotMatch(controlSource, /<el-select/);
  assert.match(controlSource, /AI 辅助疑难识别/);
  assert.doesNotMatch(
    controlSource,
    /仅在模板和规则难以判断时调用 AI|关闭后仍可识别，确认后仍会学习/
  );
  assert.doesNotMatch(controlSource, /AI 增强结构识别/);
  assert.doesNotMatch(controlSource, /自动使用/);
  assert.match(controlSource, />LLM</);
  assert.match(controlSource, />Embedding</);
  assert.match(controlSource, /llmServiceModel/);
  assert.match(controlSource, /embeddingServiceModel/);
  assert.match(controlSource, /\/config\/ai-services/);
  assert.match(controlSource, /去配置 AI 服务/);
  assert.doesNotMatch(smartFillSource, /请先选择一个可用的 LLM 服务/);
  assert.doesNotMatch(
    dataImportRecognitionSource,
    /请先选择一个可用的 LLM 服务/
  );
});
