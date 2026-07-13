import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const readSource = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

const contentLayoutSource = readSource(
  "web/src/layout/components/lay-content/index.vue"
);
const globalStyleSource = readSource("web/src/style/index.scss");
const elementPlusStyleSource = readSource("web/src/style/element-plus.scss");
const platformConfigSource = readSource("web/public/platform-config.json");
const dataImportStyleSource = readSource(
  "web/src/views/data-import/index.styles.css"
);
const smartFillStyleSource = readSource(
  "web/src/views/smart-fill/index.styles.css"
);
const smartFillSource = readSource("web/src/views/smart-fill/index.vue");
const smartFillStepsSource = readSource(
  "web/src/views/smart-fill/components/SmartFillSteps.vue"
);
const smartFillUploadStepSource = readSource(
  "web/src/views/smart-fill/components/SmartFillUploadStep.vue"
);
const smartFillTableStepSource = readSource(
  "web/src/views/smart-fill/components/SmartFillTableStep.vue"
);
const smartFillMatchStepSource = readSource(
  "web/src/views/smart-fill/components/SmartFillMatchStep.vue"
);
const smartFillPreviewStepSource = readSource(
  "web/src/views/smart-fill/components/SmartFillPreviewStep.vue"
);
const matchPreviewDataTableSource = readSource(
  "web/src/views/smart-fill/components/MatchPreviewDataTable.vue"
);
const matchPreviewTableSource = readSource(
  "web/src/views/smart-fill/components/MatchPreviewTable.vue"
);
const matchPreviewTableStyleSource = readSource(
  "web/src/views/smart-fill/components/MatchPreviewTable.styles.css"
);
const scoreDetailCandidateListSource = readSource(
  "web/src/views/smart-fill/components/ScoreDetailCandidateList.vue"
);
const executionHistorySource = readSource(
  "web/src/views/other/execution-history/index.vue"
);
const executionHistorySmartFillPlaybackSource = readSource(
  "web/src/views/other/execution-history/components/ExecutionHistorySmartFillPlayback.vue"
);
const executionHistoryBatchReplyDetailSource = readSource(
  "web/src/views/other/execution-history/components/ExecutionHistoryBatchReplyDetail.vue"
);
const fileCompareSource = readSource("web/src/views/file-compare/index.vue");
const unifiedDiffViewSource = readSource(
  "web/src/views/file-compare/components/UnifiedDiffView.vue"
);
const compareTableGridSource = readSource(
  "web/src/views/file-compare/components/CompareTableGrid.vue"
);
const dashboardPageSource = readSource("web/src/views/dashboard/index.vue");
const batchReplySource = readSource("web/src/views/batch-reply/index.vue");
const batchReplyStyleSource = readSource(
  "web/src/views/batch-reply/index.styles.css"
);
const batchReplyDuplicateDialogSource = readSource(
  "web/src/views/batch-reply/components/DuplicateResolutionDialog.vue"
);
const batchReplySourceUploadPanelSource = readSource(
  "web/src/views/batch-reply/components/SourceUploadPanel.vue"
);
const batchReplyTargetFilesPanelSource = readSource(
  "web/src/views/batch-reply/components/TargetFilesPanel.vue"
);
const dataImportSource = readSource("web/src/views/data-import/index.vue");
const tablePreviewSource = readSource(
  "web/src/views/data-import/components/TablePreview.vue"
);
const dataImportFileUploadSource = readSource(
  "web/src/views/data-import/components/FileUpload.vue"
);
const dataImportUploadStepSource = readSource(
  "web/src/views/data-import/components/DataImportStepUpload.vue"
);
const dataImportTableSelectStepSource = readSource(
  "web/src/views/data-import/components/DataImportStepTableSelect.vue"
);
const dataImportMappingStepSource = readSource(
  "web/src/views/data-import/components/DataImportStepMapping.vue"
);
const dataImportConfirmStepSource = readSource(
  "web/src/views/data-import/components/DataImportStepConfirm.vue"
);
const dataImportTargetStepSource = readSource(
  "web/src/views/data-import/components/DataImportStepTarget.vue"
);
const customerPageSource = readSource(
  "web/src/views/base-data/customers/index.vue"
);
const processPageSource = readSource(
  "web/src/views/base-data/processes/index.vue"
);
const machineModelPageSource = readSource(
  "web/src/views/base-data/machine-models/index.vue"
);
const specsPageSource = readSource("web/src/views/base-data/specs/index.vue");
const systemUsersSource = readSource(
  "web/src/views/config/system-users/index.vue"
);
const promptTemplatesSource = readSource(
  "web/src/views/config/prompt-templates/index.vue"
);
const authRolesSource = readSource("web/src/views/config/auth-roles/index.vue");
const permissionsSource = readSource(
  "web/src/views/rbac/permissions/index.vue"
);
const auditLogsSource = readSource("web/src/views/other/audit-logs/index.vue");
const aiServicesSource = readSource(
  "web/src/views/config/ai-services/index.vue"
);
const orgUnitsSource = readSource("web/src/views/config/org-units/index.vue");
const columnMappingRulesSource = readSource(
  "web/src/views/config/column-mapping-rules/index.vue"
);
const smartStructureRoutingRulesSource = readSource(
  "web/src/views/config/smart-structure-routing-rules/index.vue"
);
const databaseBackupSource = readSource(
  "web/src/views/config/database-backup/index.vue"
);
const embeddingCacheWarmupSource = readSource(
  "web/src/views/config/embedding-cache-warmup/index.vue"
);
const smartFillMatchConfigSource = readSource(
  "web/src/views/smart-fill/components/MatchConfig.vue"
);
const promptTemplatesPageSource = promptTemplatesSource;
const authRolesPageSource = authRolesSource;
const permissionsPageSource = permissionsSource;
const loginStyleSource = readSource("web/src/style/login.css");
const welcomeSource = readSource("web/src/views/welcome/index.vue");
const smartStructureConfirmCardSource = readSource(
  "web/src/views/shared/SmartStructureConfirmCard.vue"
);
const legacyDesignSystemSource = readSource(
  "design-system/acceptance-specification-system/MASTER.md"
);
const tokensPath = resolve(process.cwd(), "web/src/style/tokens.scss");
const simpleCrudPages = [
  customerPageSource,
  processPageSource,
  machineModelPageSource
];
const filterFormPages = [
  systemUsersSource,
  promptTemplatesSource,
  authRolesSource,
  permissionsSource,
  executionHistorySource,
  auditLogsSource,
  smartFillMatchConfigSource
];
const headerFilterPages = [
  systemUsersSource,
  promptTemplatesPageSource,
  authRolesPageSource,
  permissionsPageSource,
  executionHistorySource,
  auditLogsSource
];
const managementSummaryPages = [
  dashboardPageSource,
  dataImportSource,
  smartFillSource,
  batchReplySource,
  ...simpleCrudPages,
  specsPageSource,
  systemUsersSource,
  promptTemplatesPageSource,
  executionHistorySource,
  auditLogsSource,
  aiServicesSource,
  orgUnitsSource,
  columnMappingRulesSource,
  smartStructureRoutingRulesSource,
  databaseBackupSource,
  embeddingCacheWarmupSource
];
const smartFillStepSources = [
  smartFillUploadStepSource,
  smartFillTableStepSource,
  smartFillMatchStepSource,
  smartFillPreviewStepSource
];
const dataImportStepSources = [
  dataImportUploadStepSource,
  dataImportTableSelectStepSource,
  dataImportMappingStepSource,
  dataImportConfirmStepSource,
  dataImportTargetStepSource
];
const touchedDecisionStyleSources = [
  matchPreviewTableStyleSource,
  dataImportStyleSource,
  scoreDetailCandidateListSource
];

test("全局设计令牌文件必须存在并定义中性主题变量", () => {
  assert.equal(existsSync(tokensPath), true);
  const tokensSource = readFileSync(tokensPath, "utf8");

  assert.match(tokensSource, /--app-bg-page:\s*#f5f6f8/i);
  assert.match(tokensSource, /--app-text-primary:\s*#111827/i);
  assert.match(tokensSource, /--app-primary:\s*#7c3aed/i);
  assert.match(tokensSource, /--app-decision-auto:\s*var\(--app-success\)/);
});

test("主内容容器不应使用 24px 外边距挤占业务首屏", () => {
  assert.doesNotMatch(
    contentLayoutSource,
    /\.main-content\s*\{[^}]*margin:\s*24px/s
  );
});

test("主内容容器外边距不应超过 12px", () => {
  assert.match(contentLayoutSource, /\.main-content\s*\{[^}]*margin:\s*12px/s);
  assert.doesNotMatch(
    contentLayoutSource,
    /\.main-content\s*\{[^}]*margin:\s*16px/s
  );
});

test("全局页面容器应使用紧凑间距", () => {
  assert.match(globalStyleSource, /\.page\s*\{[^}]*gap:\s*12px/s);
  assert.match(globalStyleSource, /\.page\s*\{[^}]*padding:\s*0/s);
  assert.doesNotMatch(globalStyleSource, /\.page\s*\{[^}]*padding:\s*24px/s);
});

test("Element Plus 全局主题应使用中性令牌与 32px 控件密度", () => {
  assert.match(globalStyleSource, /@use\s+"tokens"/);
  assert.match(globalStyleSource, /--el-component-size:\s*32px/);
  assert.match(
    globalStyleSource,
    /--el-text-color-primary:\s*var\(--app-text-primary\)/
  );
  assert.match(globalStyleSource, /--el-table-header-bg-color:\s*#f9fafb/i);
  assert.doesNotMatch(globalStyleSource, /--el-component-size:\s*36px/);
  assert.doesNotMatch(
    globalStyleSource,
    /--el-text-color-primary:\s*var\(--color-text\)/
  );
  assert.doesNotMatch(
    globalStyleSource,
    /--el-table-header-bg-color:\s*#f3e8ff/i
  );
});

test("卡片与表格应采用紧凑密度且不使用 hover 抬升", () => {
  assert.match(
    elementPlusStyleSource,
    /\.el-card__body\s*\{[^}]*padding:\s*12px/s
  );
  assert.match(
    elementPlusStyleSource,
    /\.el-table \.el-table__cell\s*\{[^}]*padding:\s*6px 0/s
  );
  assert.doesNotMatch(
    elementPlusStyleSource,
    /\.el-card:hover\s*\{[^}]*box-shadow/s
  );
  assert.doesNotMatch(
    elementPlusStyleSource,
    /\.el-card__body\s*\{[^}]*padding:\s*16px/s
  );
  assert.doesNotMatch(
    elementPlusStyleSource,
    /\.el-table \.el-table__cell\s*\{[^}]*padding:\s*8px 0/s
  );
});

test("页脚默认应隐藏以释放首屏内容空间", () => {
  const config = JSON.parse(platformConfigSource) as { HideFooter?: boolean };

  assert.equal(config.HideFooter, true);
});

test("流程页顶部和说明区不应保留大段留白", () => {
  for (const source of [dataImportStyleSource, smartFillStyleSource]) {
    assert.doesNotMatch(source, /padding:\s*24px;/);
    assert.doesNotMatch(source, /padding:\s*20px 0;/);
    assert.doesNotMatch(source, /margin-bottom:\s*24px;/);
  }
});

test("向导页内容区不应通过固定 500px 最小高度空撑", () => {
  for (const source of [dataImportStyleSource, smartFillStyleSource]) {
    assert.doesNotMatch(source, /\.step-content\s*\{[^}]*min-height:\s*500px/s);
    assert.match(source, /\.step-content\s*\{[^}]*min-height:\s*0/s);
  }
});

test("向导页操作区应采用紧凑间距", () => {
  assert.doesNotMatch(
    smartFillStyleSource,
    /\.step-actions\s*\{[^}]*margin-top:\s*32px/s
  );
  assert.doesNotMatch(
    smartFillStyleSource,
    /\.step-actions\s*\{[^}]*padding-top:\s*16px/s
  );
  assert.match(
    smartFillStyleSource,
    /\.step-actions\s*\{[^}]*margin-top:\s*12px/s
  );
  assert.match(
    smartFillStyleSource,
    /\.step-actions\s*\{[^}]*padding-top:\s*12px/s
  );
  assert.doesNotMatch(
    dataImportStyleSource,
    /\.data-import-body\s*\{[^}]*padding-bottom:\s*84px/s
  );
});

test("智能填充预览应使用页面单一纵向滚动，避免内外滚动区冲突", () => {
  assert.doesNotMatch(matchPreviewDataTableSource, /max-height="500"/);
  assert.doesNotMatch(tablePreviewSource, /max-height="400"/);
  assert.doesNotMatch(matchPreviewDataTableSource, /height="100%"/);
  assert.doesNotMatch(
    matchPreviewTableStyleSource,
    /height:\s*calc\(100vh|max-height:\s*calc\(100vh|min-height:\s*(420|560)px/
  );
  assert.match(tablePreviewSource, /height="100%"/);
});

test("智能填充预览分页默认应与可视高度匹配", () => {
  assert.match(matchPreviewTableSource, /const pageSize = ref\(50\)/);
  assert.match(
    matchPreviewTableSource,
    /const pageSizeOptions = \[20, 50, 100, 200\]/
  );
  assert.doesNotMatch(matchPreviewTableSource, /const pageSize = ref\(100\)/);
  assert.doesNotMatch(matchPreviewTableSource, /\[50, 100, 200, 500\]/);
});

test("匹配预览决策标签应使用业务令牌且不使用 important 对抗主题", () => {
  assert.match(matchPreviewTableStyleSource, /--app-decision-auto/);
  assert.match(matchPreviewTableStyleSource, /--app-decision-review/);
  assert.match(matchPreviewTableStyleSource, /--app-decision-reject/);
  assert.doesNotMatch(matchPreviewTableStyleSource, /!important/);
});

test("已触达核心流程样式不得继续使用 Element Plus 旧状态色", () => {
  for (const source of touchedDecisionStyleSources) {
    assert.doesNotMatch(source, /#67c23a/i);
    assert.doesNotMatch(source, /#e6a23c/i);
    assert.doesNotMatch(source, /#f56c6c/i);
    assert.match(source, /--app-(decision|success|warning|danger|primary)/);
  }
});

test("简单 CRUD 页应使用全高骨架与单行工具栏", () => {
  for (const source of simpleCrudPages) {
    assert.match(source, /<div class="page page--fill simple-crud-page">/);
    assert.doesNotMatch(source, /class="page-subtitle"/);
    assert.doesNotMatch(source, /<el-card class="mb-4">/);
    assert.match(source, /<div class="simple-crud-toolbar">/);
    assert.doesNotMatch(source, /class="simple-crud-toolbar__title"/);
    assert.match(source, /class="simple-crud-toolbar__right"/);
    assert.match(source, /<el-card class="table-card" shadow="never">/);
    assert.match(source, /<div class="table-region">/);
    assert.match(source, /height="100%"/);
  }
});

test("简单 CRUD 页不应显示 PureTableBar 工具按钮", () => {
  for (const source of simpleCrudPages) {
    assert.doesNotMatch(source, /PureTableBar/);
    assert.doesNotMatch(source, /tableColumns/);
    assert.doesNotMatch(source, /@refresh="loadData"/);
  }
});

test("简单 CRUD 工具栏换行时不应拆散搜索表单和操作按钮", () => {
  assert.match(
    globalStyleSource,
    /\.simple-crud-toolbar\s*\{[^}]*flex-wrap:\s*wrap/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-toolbar\s*\{[^}]*justify-content:\s*flex-start/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\s*\{[^}]*flex-wrap:\s*wrap/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\s*\{[^}]*justify-content:\s*flex-start/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\.el-form--inline\s+\.el-form-item\s*\{[^}]*flex-shrink:\s*0/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\.el-form--inline\s+\.el-form-item\s*\{[^}]*margin-bottom:\s*0/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\.el-form--inline\s+\.el-form-item:last-child\s*\{[^}]*display:\s*flex/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-search\.el-form--inline\s+\.el-form-item:last-child\s+\.el-form-item__content\s*\{[^}]*gap:\s*8px/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-actions\s*\{[^}]*flex-wrap:\s*wrap/s
  );
  assert.match(
    globalStyleSource,
    /\.simple-crud-toolbar__right\s*\{[^}]*justify-content:\s*flex-start/s
  );
});

test("所有筛选表单换行时不应拆散搜索重置按钮", () => {
  assert.match(globalStyleSource, /\.filter-form\s*\{[^}]*flex-wrap:\s*wrap/s);
  assert.match(
    globalStyleSource,
    /\.filter-form\.el-form--inline\s+\.el-form-item\s*\{[^}]*flex-shrink:\s*0/s
  );
  assert.match(
    globalStyleSource,
    /\.filter-form\.el-form--inline\s+\.el-form-item:last-child\s*\{[^}]*flex-wrap:\s*nowrap/s
  );
  assert.match(
    globalStyleSource,
    /\.filter-form\.el-form--inline\s+\.el-form-item:last-child\s+\.el-form-item__content\s*\{[^}]*gap:\s*8px/s
  );

  for (const source of filterFormPages) {
    assert.match(source, /class="[^"]*\bfilter-form\b[^"]*"/);
  }
});

test("列表页筛选栏应放在表格卡头同一行左侧，不再单独占用筛选卡", () => {
  assert.match(
    globalStyleSource,
    /\.list-card-toolbar\s*\{[^}]*justify-content:\s*flex-start/s
  );
  assert.match(
    globalStyleSource,
    /\.list-card-toolbar__right\s*\{[^}]*justify-content:\s*flex-start/s
  );

  for (const source of headerFilterPages.filter(
    source => source !== executionHistorySource
  )) {
    assert.doesNotMatch(source, /<el-card class="mb-4">\s*<el-form/s);
    assert.match(source, /<template #header>/);
    assert.doesNotMatch(source, /<div class="list-card-toolbar">\s*<span>/);
    assert.match(source, /class="[^"]*\bfilter-form\b[^"]*"/);
  }

  for (const source of [executionHistorySource, auditLogsSource]) {
    assert.doesNotMatch(source, /<el-card class="toolbar-card">\s*<el-form/s);
  }
});

test("权限中心用户与角色页面应显示页面标题", () => {
  assert.match(
    systemUsersSource,
    /<div class="page-title">系统用户管理<\/div>/
  );
  assert.match(authRolesSource, /<div class="page-title">角色管理<\/div>/);
});

test("管理与配置列表页不应保留重复说明和卡头区域名", () => {
  assert.doesNotMatch(globalStyleSource, /\.page-subtitle\s*\{/);

  for (const source of managementSummaryPages) {
    assert.doesNotMatch(source, /class="page-subtitle"/);
  }

  for (const source of [...simpleCrudPages, ...headerFilterPages]) {
    assert.doesNotMatch(source, /simple-crud-toolbar__title/);
    assert.doesNotMatch(source, /<div class="list-card-toolbar">\s*<span>/);
  }

  assert.doesNotMatch(
    smartStructureRoutingRulesSource,
    /<span>路由规则<\/span>/
  );
  assert.doesNotMatch(
    columnMappingRulesSource,
    /<span>列映射规则（全局）<\/span>/
  );
});

test("简单 CRUD 页分页默认应使用 50 并收敛选项", () => {
  for (const source of simpleCrudPages) {
    assert.match(source, /pageSize:\s*50/);
    assert.match(source, /:page-sizes="\[20, 50, 100, 200\]"/);
    assert.doesNotMatch(source, /pageSize:\s*20/);
    assert.doesNotMatch(source, /:page-sizes="\[10, 20, 50, 100\]"/);
  }
});

test("执行记录页应使用全高骨架并移除固定详情表高度", () => {
  assert.match(
    executionHistorySource,
    /<div class="page page--fill execution-history-page">/
  );
  assert.doesNotMatch(executionHistorySource, /task-control-pagination/);
  assert.doesNotMatch(executionHistorySource, /handlePageChange/);
  assert.doesNotMatch(executionHistorySource, /handleSizeChange/);
  assert.doesNotMatch(executionHistorySource, />任务详情</);
  assert.doesNotMatch(executionHistorySource, /summary-grid/);
  assert.match(executionHistorySource, /class="task-control-row"/);
  assert.doesNotMatch(executionHistorySource, /class="task-select-block"/);
  assert.doesNotMatch(
    executionHistorySmartFillPlaybackSource,
    /playback-detail/
  );
  assert.doesNotMatch(executionHistorySmartFillPlaybackSource, /ScoreDetail/);

  for (const source of [
    executionHistorySmartFillPlaybackSource,
    executionHistoryBatchReplyDetailSource
  ]) {
    assert.doesNotMatch(source, /max-height="(560|620)"/);
    assert.match(source, /statusFilter/);
    assert.match(source, /pagedRows/);
    assert.match(source, /const pageSize = ref\(50\)/);
    assert.match(source, /<el-tabs/);
    assert.match(source, /<el-pagination/);
    assert.match(source, /class="result-pagination"/);
    assert.match(source, /height="100%"/);
    assert.doesNotMatch(source, /<el-form-item label="文件">/);
    assert.doesNotMatch(source, /<el-form-item label="表格">/);
  }
});

test("文件对比页应复用页面骨架、视口高度和 diff 令牌", () => {
  assert.match(
    fileCompareSource,
    /<div class="page page--fill file-compare-page">/
  );
  assert.doesNotMatch(fileCompareSource, /<div class="compare-page">/);
  assert.doesNotMatch(fileCompareSource, /height:\s*min\(62vh,\s*640px\)/);
  assert.doesNotMatch(fileCompareSource, /height:\s*520px/);
  assert.match(fileCompareSource, /height:\s*100%/);
  assert.match(fileCompareSource, /--app-diff-add-bg/);
  assert.match(fileCompareSource, /--app-diff-del-bg/);

  for (const source of [unifiedDiffViewSource, compareTableGridSource]) {
    assert.doesNotMatch(source, /#e6ffec|#ffeef0|#acf2bd|#fdb8c0/i);
    assert.match(source, /--app-diff-/);
  }
});

test("批量回复页应使用紧凑令牌化头部", () => {
  assert.match(
    batchReplySource,
    /<div class="page page--fill(?: page-shell)? batch-reply-page">/
  );
  assert.doesNotMatch(batchReplySource, /page-header__eyebrow/);
  assert.doesNotMatch(batchReplySource, /<h1>批量回复<\/h1>/);
  assert.match(batchReplyStyleSource, /--app-primary/);
  assert.match(batchReplyStyleSource, /--app-bg-card/);
  assert.doesNotMatch(batchReplyStyleSource, /min-height:\s*90px/);
  assert.doesNotMatch(batchReplyStyleSource, /font-size:\s*30px/);
  assert.doesNotMatch(batchReplyStyleSource, /#2f6bb2|#173d73|#2158a8/i);
});

test("智能填充步骤条应合并进页头且自身不再是卡片", () => {
  assert.match(
    smartFillSource,
    /<div class="page-header">[\s\S]*<SmartFillSteps :steps="steps" :current-step="currentStep" \/>[\s\S]*<\/div>/
  );
  assert.doesNotMatch(
    smartFillSource,
    /\n    <\/div>\r?\n    <SmartFillSteps :steps="steps" :current-step="currentStep" \/>/
  );
  assert.match(smartFillStepsSource, /class="wizard-steps"/);
  assert.doesNotMatch(smartFillStepsSource, /<el-card/);
  assert.doesNotMatch(smartFillStepsSource, /class="mb-4"/);
});

test("数据导入步骤条应合并进页头并移除独立 affix 卡片", () => {
  assert.match(
    dataImportSource,
    /<div class="page-header">[\s\S]*<el-steps[\s\S]*<\/el-steps>[\s\S]*<\/div>/
  );
  assert.doesNotMatch(dataImportSource, /steps-affix/);
  assert.doesNotMatch(dataImportSource, /steps-card/);
  assert.doesNotMatch(dataImportSource, /<el-affix/);
});

test("向导步骤组件不应重复展示步骤标题和长说明", () => {
  for (const source of [...smartFillStepSources, ...dataImportStepSources]) {
    assert.doesNotMatch(source, /class="step-title"/);
    assert.doesNotMatch(source, /class="step-desc/);
  }
  assert.doesNotMatch(dataImportSource, /class="step-title"/);
  assert.doesNotMatch(dataImportSource, /class="step-desc/);
});

test("上传区入口应复用统一上传区类和令牌化样式", () => {
  for (const source of [
    dataImportFileUploadSource,
    batchReplySourceUploadPanelSource,
    batchReplyTargetFilesPanelSource
  ]) {
    assert.match(source, /class="app-upload-area|<AppUploadZone/);
  }
  assert.doesNotMatch(dataImportFileUploadSource, /class="upload-area"/);
  assert.doesNotMatch(batchReplySourceUploadPanelSource, /class="upload-area"/);
  assert.doesNotMatch(batchReplyTargetFilesPanelSource, /class="upload-area"/);
  assert.doesNotMatch(dataImportFileUploadSource, /#e4d7fb|#409EFF/i);
  assert.match(dataImportFileUploadSource, /--app-primary|--el-color-primary/);
  assert.match(
    batchReplyStyleSource,
    /\.app-upload-area :deep\(\.el-upload-dragger\)/
  );
});

test("批量回复重复处理弹窗应使用响应式规格宽度", () => {
  assert.match(
    batchReplyDuplicateDialogSource,
    /width="min\(960px, calc\(100vw - 32px\)\)"/
  );
  assert.doesNotMatch(batchReplyDuplicateDialogSource, /width="960px"/);
});

test("配置页和权限页下拉应使用规范宽度档位与正确 popper", () => {
  assert.doesNotMatch(systemUsersSource, /class="w-\[180px\]"/);
  assert.match(systemUsersSource, /class="search-select search-select--200"/);
  assert.match(systemUsersSource, /popper-class="config-select-popper"/);

  assert.doesNotMatch(permissionsSource, /class="w-\[180px\]"/);
  assert.match(permissionsSource, /class="search-select search-select--200"/);
  assert.match(permissionsSource, /popper-class="app-select-popper"/);
  assert.doesNotMatch(permissionsSource, /popper-class="config-select-popper"/);
});

test("登录页与欢迎页不应继续使用旧紫色模板色值", () => {
  assert.doesNotMatch(loginStyleSource, /#4c1d95|#f9f5ff/i);
  assert.match(loginStyleSource, /--app-text-primary/);
  assert.match(loginStyleSource, /--app-bg-page/);
  assert.doesNotMatch(welcomeSource, /#6b7280/i);
  assert.match(welcomeSource, /--app-text-secondary/);
});

test("智能结构确认卡片应继续使用设计令牌而非散装灰蓝色板", () => {
  assert.doesNotMatch(
    smartStructureConfirmCardSource,
    /#fff|#dce4ee|#1f3349|#6b7785|#7b8794|#808b98|#fbfcfd|#e5ebf2/i
  );
  assert.match(smartStructureConfirmCardSource, /--app-bg-card/);
  assert.match(smartStructureConfirmCardSource, /--app-border/);
  assert.match(smartStructureConfirmCardSource, /--app-text-primary/);
  assert.match(smartStructureConfirmCardSource, /--app-text-secondary/);
});

test("数据导入差异和确认区应继续使用令牌，避免旧蓝灰色板回流", () => {
  assert.doesNotMatch(
    dataImportStyleSource,
    /#8a5a00|#f8fbff|#dbe7f8|#7b8794|#243447|#6d28d9|#e5ebf2|#f7fbff|#1d4ed8|#64748b|#2563eb|#475569/i
  );
  assert.match(dataImportStyleSource, /--app-primary/);
  assert.match(dataImportStyleSource, /--app-warning/);
  assert.match(dataImportStyleSource, /--app-border/);
  assert.match(dataImportStyleSource, /--app-bg-card/);
});

test("旧 design-system MASTER 必须明确废弃，避免再次接入紫色活动模板", () => {
  assert.match(legacyDesignSystemSource, /已废弃|Deprecated/i);
  assert.match(legacyDesignSystemSource, /web\/src\/style\/tokens\.scss/);
  assert.match(legacyDesignSystemSource, /不要|Do not/i);
});
