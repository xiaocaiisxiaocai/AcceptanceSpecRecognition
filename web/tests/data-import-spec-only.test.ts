import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readProjectFile = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

test("导入确认页应提示仅规格导入会用规格补齐项目", () => {
  const source = readProjectFile(
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  );

  assert.match(source, /项目将使用规格内容自动补齐/);
  assert.match(source, /确认没有独立项目列/);
  assert.match(source, /跨项目匹配风险/);
});

test("仅规格提示应只在实际按规格回填项目时展示", () => {
  const panelSource = readProjectFile(
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  );
  const previewSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportPreviewSelection.ts"
  );
  const batchExecutionSource = readProjectFile(
    "web/src/views/data-import/composables/useDataImportBatchExecution.ts"
  );

  assert.match(panelSource, /shouldBackfillProjectFromSpecification/);
  assert.match(previewSource, /shouldBackfillProjectFromSpecification/);
  assert.match(batchExecutionSource, /shouldBackfillProjectFromSpecification/);
});
