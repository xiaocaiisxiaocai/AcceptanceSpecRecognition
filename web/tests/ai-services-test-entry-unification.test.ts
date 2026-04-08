import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const aiServicesPageSource = readFileSync(
  resolve(process.cwd(), "src/views/config/ai-services/index.vue"),
  "utf8"
);

test("AI服务配置页只保留单一完整测试入口", () => {
  assert.doesNotMatch(aiServicesPageSource, /AiServiceConnectionTestMode/);
  assert.doesNotMatch(aiServicesPageSource, /quick-\$\{row\.id\}/);
  assert.doesNotMatch(aiServicesPageSource, /handleTest\(.*["']quick["']\)/);
  assert.match(aiServicesPageSource, /@click="handleTest\(row\)"/);
});
