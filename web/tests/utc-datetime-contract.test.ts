import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readWebSource = (relativePath: string) =>
  readFileSync(resolve(process.cwd(), `web/src/${relativePath}`), "utf8");

const persistedDateTimeViews = [
  "views/base-data/customers/index.vue",
  "views/base-data/machine-models/index.vue",
  "views/base-data/processes/index.vue",
  "views/base-data/specs/components/SpecDuplicateDialog.vue",
  "views/base-data/specs/components/SpecSemanticSearchDialog.vue",
  "views/base-data/specs/components/SpecTable.vue",
  "views/config/database-backup/index.vue",
  "views/config/embedding-cache-warmup/index.vue",
  "views/config/prompt-templates/index.vue",
  "views/config/system-users/index.vue",
  "views/dashboard/index.vue",
  "views/other/audit-logs/index.vue",
  "views/other/execution-history/executionHistory.formatters.ts"
];

test("数据库时间展示应统一使用 API UTC 格式化器", () => {
  for (const relativePath of persistedDateTimeViews) {
    const source = readWebSource(relativePath);
    assert.match(source, /(?:format|parse)ApiUtcDateTime/, relativePath);
  }
});

test("验收规格各入口不应直接按本地时间解释 API 时间", () => {
  for (const relativePath of persistedDateTimeViews.filter(path =>
    path.includes("base-data/specs/")
  )) {
    const source = readWebSource(relativePath);
    assert.doesNotMatch(source, /new Date\(value\)/, relativePath);
    assert.doesNotMatch(
      source,
      /new Date\(detailData\.importedAt\)/,
      relativePath
    );
  }
});

test("审计日志查询和删除范围应转换为 UTC ISO 时间", () => {
  const source = readWebSource("views/other/audit-logs/index.vue");
  assert.match(source, /toApiUtcDateTime\(from\)/);
  assert.match(source, /toApiUtcDateTime\(to\)/);
  assert.doesNotMatch(source, /value-format="YYYY-MM-DDTHH:mm:ss"/);
});
