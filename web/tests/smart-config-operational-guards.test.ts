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
  assert.match(source, /tableCount:\s*res\.data\.length/);
});
