import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const repositoryRoot = existsSync(resolve(process.cwd(), "web/package.json"))
  ? process.cwd()
  : resolve(process.cwd(), "..");
const readProjectFile = (path: string) =>
  readFileSync(resolve(repositoryRoot, path), "utf8");

test("智能确认页应按 Sheet 页签逐张展示且保持确认卡挂载", () => {
  const pageSource = readProjectFile("web/src/views/data-import/index.vue");

  assert.match(pageSource, /const smartStructureTabItems = computed\(/);
  assert.match(
    pageSource,
    /const activeSmartStructureTab = ref<number \| undefined>\(/
  );
  const smartTabsBlock =
    pageSource.match(
      /<el-tabs\n\s*v-if="smartStructureTabItems\.length > 0"[\s\S]*?<\/el-tabs>/
    )?.[0] ?? "";
  assert.match(smartTabsBlock, /v-model="activeSmartStructureTab"/);
  assert.match(smartTabsBlock, /v-for="table in smartStructureTabItems"/);
  assert.doesNotMatch(smartTabsBlock, /\blazy\b/);
});

test("导入确认页应使用紧凑摘要操作栏承载文件信息和导入动作", () => {
  const panelSource = readProjectFile(
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  );
  assert.match(panelSource, /class="import-summary-bar"/);
  assert.match(panelSource, /class="import-summary-bar__meta"/);
  assert.match(panelSource, /class="import-summary-bar__actions"/);
  assert.match(panelSource, /\.import-summary-bar\s*\{/);
  assert.match(panelSource, /\.import-summary-bar__actions\s*\{/);
});
