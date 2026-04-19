import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

test("智能填充默认配置应将 LLM 并行数设为 8", () => {
  const currentDir = path.dirname(fileURLToPath(import.meta.url));
  const source = readFileSync(
    path.join(currentDir, "../src/api/matching.ts"),
    "utf8"
  );

  assert.match(source, /llmParallelism:\s*8,/);
});
