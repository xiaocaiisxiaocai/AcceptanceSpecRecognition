import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);

test("导入页应维护显式进度文案，避免长时间 AI 去重时只有处理中提示", () => {
  assert.match(source, /const importProgressText = ref\(""\);/);
  assert.match(source, /const importProgressDescription = computed\(\(\) =>/);
  assert.match(source, /const importPrimaryButtonText = computed\(\(\) =>/);
  assert.match(source, /const confirmDifferenceButtonText = computed\(\(\) =>/);
  assert.match(source, /const differenceDialogFooterTip = computed\(\(\) =>/);
});

test("导入页在执行中应展示进度提示面板", () => {
  assert.match(source, /v-if="importing" class="import-progress-panel"/);
  assert.match(source, /\{\{ importProgressText \}\}/);
  assert.match(source, /\{\{ importProgressDescription \}\}/);
});

test("导入按钮与重复确认按钮应复用更明确的进度文案", () => {
  assert.match(source, /\{\{\s*importPrimaryButtonText\s*\}\}/);
  assert.match(source, /\{\{\s*confirmDifferenceButtonText\s*\}\}/);
  assert.match(source, /\{\{\s*differenceDialogFooterTip\s*\}\}/);
});
