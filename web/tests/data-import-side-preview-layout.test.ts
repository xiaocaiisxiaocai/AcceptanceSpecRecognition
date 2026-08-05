import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const indexSource = fs.readFileSync(
  "web/src/views/data-import/index.vue",
  "utf8"
);
const indexStyles = fs.readFileSync(
  "web/src/views/data-import/index.styles.css",
  "utf8"
);
const confirmPanelSource = fs.readFileSync(
  "web/src/views/data-import/components/DataImportConfirmPanel.vue",
  "utf8"
);
const previewPanelSource = fs.readFileSync(
  "web/src/views/data-import/components/DataImportPreviewPanel.vue",
  "utf8"
);
const smartRecognitionSource = fs.readFileSync(
  "web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts",
  "utf8"
);
const smartConfirmTabsSource = fs.readFileSync(
  "web/src/views/shared/SmartStructureConfirmTabs.vue",
  "utf8"
);
const smartConfirmCardSource = fs.readFileSync(
  "web/src/views/shared/SmartStructureConfirmCard.vue",
  "utf8"
);
const pageComposableSource = fs.readFileSync(
  "web/src/views/data-import/composables/useDataImportPage.ts",
  "utf8"
);

test("智能确认步骤应在右侧复用独立待导入预览", () => {
  assert.match(indexSource, /import DataImportPreviewPanel/);
  assert.match(indexSource, /class="smart-confirm-workspace"/);
  assert.match(indexSource, /class="smart-confirm-workspace__preview"/);
  assert.match(indexSource, /<DataImportPreviewPanel/);
  assert.match(indexSource, /:show-preview-list="false"/);

  assert.match(confirmPanelSource, /import DataImportPreviewPanel/);
  assert.match(confirmPanelSource, /showPreviewList\?: boolean/);
  assert.match(confirmPanelSource, /v-if="showPreviewList"/);
  assert.match(previewPanelSource, /emit\("loadFullPreview"\)/);
  assert.match(previewPanelSource, /props\.importPreviewGroups/);
  assert.match(previewPanelSource, /setTimeout\(\(\) => \{/);
  assert.match(previewPanelSource, /\}, 800\)/);
  assert.match(previewPanelSource, /emit\(["']removeSelectedPreviewRows["']\)/);
  assert.match(
    previewPanelSource,
    /emit\(["']removeSinglePreviewRow["'], row\)/
  );
  assert.doesNotMatch(previewPanelSource, /PREVIEW_PAGE_SIZE/);
  assert.doesNotMatch(previewPanelSource, /previewPageMap/);
  assert.doesNotMatch(previewPanelSource, /pagedImportPreviewGroups/);
  assert.doesNotMatch(previewPanelSource, /<el-pagination/);
  assert.doesNotMatch(previewPanelSource, /import-preview-pagination/);
  assert.match(previewPanelSource, /selectedImportPreviewRowKeys/);
  assert.match(previewPanelSource, /class="preview-topbar"/);
  assert.doesNotMatch(previewPanelSource, /import-preview-note/);
});

test("有效结构草稿应合并刷新同一份预览状态", () => {
  assert.match(indexSource, /request\?\.userModifiedStructure/);
  assert.match(indexSource, /setTimeout\(\(\) => \{/);
  assert.match(
    indexSource,
    /previewSmartRecognizedTables\(buildSmartDraftPreviewTables\(\)\)/
  );
  assert.match(
    smartRecognitionSource,
    /const previewSmartRecognizedTables = async/
  );
  assert.match(
    smartRecognitionSource,
    /previewSmartRecognizedTables,[\s\S]*handleSmartTableImportSelectionChange/
  );
});

test("全量预览遇到结构刷新时应基于最新配置重试", () => {
  assert.match(
    pageComposableSource,
    /type FullPreviewLoadResult = "success" \| "retry" \| "failed"/
  );
  assert.match(pageComposableSource, /message\.includes\("预览配置已更新"\)/);
  assert.match(
    pageComposableSource,
    /for \(let attempt = 0; attempt < 2; attempt \+= 1\)/
  );
});

test("双栏布局应提供粘性右栏和窄屏上下降级", () => {
  const workspaceRule = indexStyles.match(
    /\.smart-confirm-workspace\s*\{([^}]*)\}/
  )?.[1];
  assert.ok(workspaceRule, "智能确认步骤必须定义双栏工作区");
  assert.match(workspaceRule, /grid-template-columns:/);

  const previewRule = indexStyles.match(
    /\.smart-confirm-workspace__preview\s*\{([^}]*)\}/
  )?.[1];
  assert.ok(previewRule, "右侧预览必须定义独立工作区");
  assert.match(previewRule, /position:\s*sticky;/);
  assert.match(
    previewRule,
    /height:\s*min\(720px,\s*calc\(100dvh\s*-\s*316px\)\);/
  );
  assert.match(previewRule, /overflow:\s*hidden;/);
  assert.match(
    previewPanelSource,
    /\.import-preview-tabs--stacked\s*\{[\s\S]*?overflow:\s*auto;/
  );
  assert.match(
    previewPanelSource,
    /class="import-preview-table"[\s\S]*?<el-table[\s\S]*?height="100%"/
  );

  assert.match(
    indexStyles,
    /@media\s*\(width\s*<=\s*1280px\)[\s\S]*?\.smart-confirm-workspace\s*\{[\s\S]*?grid-template-columns:\s*minmax\(0,\s*1fr\);/
  );
});

test("智能确认页应隐藏重复的范围说明和文件摘要", () => {
  assert.match(indexSource, /:show-range-summary-subtitle="false"/);
  assert.match(indexSource, /:show-summary-bar="false"/);

  assert.match(smartConfirmTabsSource, /showRangeSummarySubtitle\?: boolean/);
  assert.match(
    smartConfirmTabsSource,
    /:show-range-summary-subtitle="showRangeSummarySubtitle"/
  );
  assert.match(
    smartConfirmCardSource,
    /v-if="showRangeSummarySubtitle"[\s\S]*?class="range-summary-subtitle"/
  );

  assert.match(confirmPanelSource, /showSummaryBar\?: boolean/);
  assert.match(
    confirmPanelSource,
    /v-if="showSummaryBar"\s+class="import-summary-bar"/
  );
});

test("右侧待导入清单应按区域拆分 Sheet Tab 并保持左侧工作表同步", () => {
  assert.match(
    indexSource,
    /<DataImportPreviewPanel[\s\S]*?v-model:active-table-index="activeSmartStructureTab"/
  );
  assert.match(indexSource, /<DataImportPreviewPanel[\s\S]*?tabbed-groups/);

  assert.match(previewPanelSource, /activeTableIndex\?: number/);
  assert.match(previewPanelSource, /tabbedGroups\?: boolean/);
  assert.match(
    previewPanelSource,
    /update:activeTableIndex[\s\S]*tableIndex: number/
  );
  assert.match(previewPanelSource, /<el-tabs[\s\S]*?<el-tab-pane/);
  assert.match(
    previewPanelSource,
    /v-for="group in visibleImportPreviewGroups"[\s\S]*?:name="getPreviewTabName\(group\.key\)"/
  );
  assert.match(
    previewPanelSource,
    /const previewTabNamePrefix = `\$\{useId\(\)\}/
  );
  assert.match(previewPanelSource, /const getPreviewGroupKey =/);
  assert.match(
    previewPanelSource,
    /emit\("update:activeTableIndex", group\.tableIndex\)/
  );
  assert.match(
    previewPanelSource,
    /const visibleImportPreviewGroups = computed[\s\S]*group\.tableIndex === props\.activeTableIndex/
  );
  assert.match(
    previewPanelSource,
    /v-if="visibleImportPreviewGroups\.length > 0"/
  );
  assert.match(
    previewPanelSource,
    /v-for="group in visibleImportPreviewGroups"/
  );
  assert.doesNotMatch(
    previewPanelSource,
    /requestedGroup \?\? currentGroup \?\? props\.importPreviewGroups\[0\]/
  );
  assert.match(
    previewPanelSource,
    /\.import-preview-tabs[\s\S]*?overflow:\s*hidden;/
  );
});
