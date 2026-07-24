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

  assert.match(source, /本地 Ollama 建议\s*1-4/);
});

test("智能填充全局配置应隐藏运行时托管项并保留运行时可用性判断", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const matchConfigSource = readFileSync(
    path.join(currentDir, "../src/views/smart-fill/components/MatchConfig.vue"),
    "utf8"
  );

  assert.doesNotMatch(
    matchConfigSource,
    /<el-form-item label="Embedding 服务">/
  );
  assert.doesNotMatch(matchConfigSource, /<el-form-item label="LLM 服务">/);
  assert.doesNotMatch(matchConfigSource, /<el-form-item label="过滤空行">/);
  assert.doesNotMatch(matchConfigSource, /\.automatic-service/);

  assert.match(
    matchConfigSource,
    /const embeddingSelection = ref<AiServiceSelection>/
  );
  assert.match(
    matchConfigSource,
    /const llmSelection = ref<AiServiceSelection>/
  );
  assert.match(
    matchConfigSource,
    /const hasAvailableEmbeddingService = computed/
  );
  assert.match(matchConfigSource, /const hasAvailableLlmService = computed/);
});

test("过滤空行控件应只在智能填充入口隐藏并默认保留给批量回复", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const batchConfigSource = readFileSync(
    path.join(
      currentDir,
      "../src/views/smart-fill/components/BatchTableConfig.vue"
    ),
    "utf8"
  );
  const smartFillTableStepSource = readFileSync(
    path.join(
      currentDir,
      "../src/views/smart-fill/components/SmartFillTableStep.vue"
    ),
    "utf8"
  );
  const batchReplySourceConfigSource = readFileSync(
    path.join(
      currentDir,
      "../src/views/batch-reply/components/SourceConfigPanel.vue"
    ),
    "utf8"
  );
  const batchReplyTargetConfigSource = readFileSync(
    path.join(
      currentDir,
      "../src/views/batch-reply/components/TargetFilesPanel.vue"
    ),
    "utf8"
  );

  assert.match(batchConfigSource, /showFilterEmptySourceRows\?: boolean/);
  assert.match(batchConfigSource, /showFilterEmptySourceRows:\s*true/);
  assert.match(
    batchConfigSource,
    /<el-form-item\s+v-if="props\.showFilterEmptySourceRows"\s+label="过滤空行"\s*>/
  );
  assert.match(
    smartFillTableStepSource,
    /<BatchTableConfig[\s\S]*?:show-filter-empty-source-rows="false"[\s\S]*?\/>/
  );

  assert.match(batchReplySourceConfigSource, /<BatchTableConfigPanel/);
  assert.match(batchReplyTargetConfigSource, /<BatchTableConfigPanel/);
  assert.doesNotMatch(
    `${batchReplySourceConfigSource}\n${batchReplyTargetConfigSource}`,
    /show-filter-empty-source-rows/
  );
});
