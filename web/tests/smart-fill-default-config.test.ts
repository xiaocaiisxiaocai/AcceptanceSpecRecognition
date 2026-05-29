import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

test("智能填充默认配置应保持保守的 LLM 并行数", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const source = readFileSync(
    path.join(currentDir, "../src/api/matching.ts"),
    "utf8"
  );

  assert.match(source, /llmParallelism:\s*4,/);
});

test("智能填充默认 LLM 单行超时应覆盖 Ollama 冷启动", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const source = readFileSync(
    path.join(currentDir, "../src/api/matching.ts"),
    "utf8"
  );

  assert.match(source, /llmRowTimeoutSeconds:\s*120,/);
});

test("智能填充 LLM 并行提示应给出本地 Ollama 建议范围", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const source = readFileSync(
    path.join(currentDir, "../src/views/smart-fill/components/MatchConfig.vue"),
    "utf8"
  );

  assert.match(source, /本地 Ollama 建议 1-4/);
});
