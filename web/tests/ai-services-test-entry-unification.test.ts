import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const aiServicesPageSource = readFileSync(
  resolve(process.cwd(), "web/src/views/config/ai-services/index.vue"),
  "utf8"
);
const aiServicesTableSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/config/ai-services/components/AiServiceConfigsTable.vue"
  ),
  "utf8"
);
const aiServicesStyleSource = readFileSync(
  resolve(process.cwd(), "web/src/views/config/ai-services/index.styles.css"),
  "utf8"
);
const aiServiceApiSource = readFileSync(
  resolve(process.cwd(), "web/src/api/ai-service.ts"),
  "utf8"
);

test("AI服务配置页只保留单一完整测试入口", () => {
  assert.doesNotMatch(aiServicesPageSource, /AiServiceConnectionTestMode/);
  assert.doesNotMatch(aiServicesPageSource, /quick-\$\{row\.id\}/);
  assert.doesNotMatch(aiServicesPageSource, /handleTest\(.*["']quick["']\)/);
  assert.match(aiServicesPageSource, /@test="handleTest"/);
  assert.match(aiServicesTableSource, /@click="emit\('test', row\)"/);
});

test("AI服务配置页提供禁用切换且禁用后不可测试或探测模型", () => {
  assert.match(aiServiceApiSource, /setAiServiceDisabled/);
  assert.match(aiServiceApiSource, /\/disabled/);
  assert.match(aiServicesPageSource, /handleToggleDisabled/);
  assert.match(aiServicesTableSource, /row\.isDisabled \? "启用" : "禁用"/);
  assert.match(
    aiServicesTableSource,
    /row\.isDisabled \|\| isRowLoading\(testingState, row\.id\)/
  );
  assert.match(
    aiServicesTableSource,
    /row\.isDisabled \|\| isRowLoading\(probingState, row\.id\)/
  );
});

test("AI服务排序规则应由共享 API helper 统一维护", () => {
  assert.match(
    aiServiceApiSource,
    /export const sortAiServicesByPriority = \(services: AiServiceConfig\[\]\) =>/
  );
  assert.match(
    aiServiceApiSource,
    /const priorityDiff = a\.priority - b\.priority/
  );
  assert.match(
    aiServiceApiSource,
    /parseApiUtcDateTime\(a\.updatedAt \|\| a\.createdAt\)\?\.getTime\(\)/
  );
  assert.doesNotMatch(aiServiceApiSource, /Date\.parse\(/);
});

test("AI服务操作列应保持固定宽度并且操作不换行", () => {
  assert.match(aiServicesTableSource, /label="操作"\s+width="300"/);
  assert.match(aiServicesTableSource, /class="ai-service-actions"/);
  assert.match(
    aiServicesStyleSource,
    /\.ai-service-actions\s*\{[^}]*flex-wrap:\s*nowrap;/s
  );
  assert.match(
    aiServicesStyleSource,
    /\.ai-service-actions\s*\{[^}]*white-space:\s*nowrap;/s
  );
});
