import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

const confirmPanelSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportConfirmPanel.vue"
  ),
  "utf8"
);

const confirmCardSource = readFileSync(
  resolve(process.cwd(), "web/src/views/shared/SmartStructureConfirmCard.vue"),
  "utf8"
);
const confirmTabsSource = readFileSync(
  resolve(process.cwd(), "web/src/views/shared/SmartStructureConfirmTabs.vue"),
  "utf8"
);
const dataImportSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.vue"),
  "utf8"
);
const dataImportStyleSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/index.styles.css"),
  "utf8"
);
const summaryBannerSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/SmartStructureSummaryBanner.vue"
  ),
  "utf8"
);
const recognitionComposableSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/useSmartStructureRecognition.ts"
  ),
  "utf8"
);
const uploadStepSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportStepUpload.vue"
  ),
  "utf8"
);
const dataImportTargetSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/composables/useDataImportTarget.ts"
  ),
  "utf8"
);
const rangeEditorSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/SmartStructureRangeEditorDrawer.vue"
  ),
  "utf8"
);
const dataImportPageSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/composables/useDataImportPage.ts"
  ),
  "utf8"
);
const dataImportHelpersSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/dataImport.helpers.ts"),
  "utf8"
);

test("数据导入确认页应把导入设置和待导入清单折叠，避免预览页被明细长表撑散", () => {
  assert.match(confirmPanelSource, /<el-collapse[\s\S]*confirm-panel-collapse/);
  assert.match(confirmPanelSource, /name="duplicate-ai"/);
  assert.match(confirmPanelSource, /name="preview-list"/);
  assert.match(confirmPanelSource, /导入设置/);
  assert.match(confirmPanelSource, /待导入清单/);
});

test("AI 去重关闭时应收起无效配置，并由确认组件自身承载样式", () => {
  assert.match(
    confirmPanelSource,
    /v-if="!duplicateAiConfig\.enableSemanticDuplicateCheck"[\s\S]*AI 去重当前关闭/
  );
  assert.match(
    confirmPanelSource,
    /<el-form[\s\S]*v-else[\s\S]*label-position="top"/
  );
  assert.match(confirmPanelSource, /\.duplicate-ai-panel__mark/);
  assert.doesNotMatch(dataImportStyleSource, /\.duplicate-ai-panel/);
});

test("待导入清单应使用数量概览和移出语义，避免重复标题与删除歧义", () => {
  assert.match(confirmPanelSource, /class="preview-metric primary"/);
  assert.match(confirmPanelSource, /移出所选/);
  assert.match(confirmPanelSource, /移出仅影响本次导入，不会修改原文件/);
  assert.doesNotMatch(confirmPanelSource, /批量删除/);
  assert.doesNotMatch(confirmPanelSource, /待导入数据清单/);
});

test("展开待导入清单时应自动补拉完整预览并复用并发请求", () => {
  assert.match(
    confirmPanelSource,
    /names\.includes\("preview-list"\)[\s\S]*previewLoadState\.hasPartialPreview[\s\S]*emit\("loadFullPreview"\)/
  );
  assert.match(confirmPanelSource, /@change="handleCollapseChange"/);
  assert.equal(
    dataImportSource.match(/@load-full-preview="ensureFullPreviewDataLoaded"/g)
      ?.length,
    2
  );
  assert.match(
    dataImportPageSource,
    /let fullPreviewLoadPromise: Promise<boolean> \| null = null/
  );
  assert.match(
    dataImportPageSource,
    /if \(fullPreviewLoadPromise\) \{\s*return fullPreviewLoadPromise;\s*\}/
  );
  assert.match(
    dataImportPageSource,
    /previewLoadState,\s*ensureFullPreviewDataLoaded,/
  );
});

test("待导入清单不应使用内部纵向滚动，避免鼠标滚轮被表格区域截获", () => {
  assert.doesNotMatch(confirmPanelSource, /max-height="280"/);
});

test("未导入详情应折叠重复多行表头并保持单行显示", () => {
  assert.match(confirmPanelSource, /class="skipped-rows-table"/);
  assert.match(
    confirmPanelSource,
    /\.skipped-rows-table :deep\(\.el-table__header \.cell\)[\s\S]*white-space:\s*nowrap/
  );
  assert.match(
    dataImportHelpersSource,
    /buildSkippedPreviewColumns\(headers, maxColumnCount\)/
  );
  assert.match(confirmPanelSource, /col\.indexes\.join\('-'\)/);
  assert.match(confirmPanelSource, /mergeSkippedPreviewCellValues/);
});

test("智能结构确认卡片应常驻展示多区域范围，并移除重复高级字段", () => {
  assert.doesNotMatch(confirmCardSource, /defaultExpanded\?: boolean/);
  assert.match(confirmCardSource, /class="range-summary-panel"/);
  assert.match(confirmCardSource, /effectiveRowCount/);
  assert.match(confirmCardSource, /ignoredRowCount/);
  assert.doesNotMatch(confirmCardSource, />高级设置|收起高级设置</);
  assert.doesNotMatch(confirmCardSource, /v-show="detailVisible"/);
});

test("智能结构确认卡片应把项目列放在规格列之前", () => {
  const summaryStart = confirmCardSource.indexOf("const rangeSummaryFields");
  const summaryEnd = confirmCardSource.indexOf("const regionSummaryItems");
  const summarySource = confirmCardSource.slice(summaryStart, summaryEnd);
  const projectIndex = summarySource.indexOf('{ label: "项目列"');
  const specificationIndex = summarySource.indexOf('{ label: "规格列"');

  assert.ok(summaryStart >= 0 && summaryEnd > summaryStart);
  assert.ok(projectIndex >= 0, "范围摘要缺少项目列");
  assert.ok(specificationIndex >= 0, "范围摘要缺少规格列");
  assert.ok(projectIndex < specificationIndex, "项目列必须位于规格列之前");
});

test("确认卡应避免重复展示映射，并仅在需要时展示识别依据", () => {
  assert.doesNotMatch(confirmCardSource, /card-summary-strip/);
  assert.match(confirmCardSource, /const showRecognitionEvidence = computed/);
  assert.match(
    confirmCardSource,
    /v-if="showRecognitionEvidence"[\s\S]*class="headers-preview"/
  );
  assert.match(
    confirmCardSource,
    /v-if="showRecognitionEvidence && table\.fields/
  );
});

test("范围摘要应读取用户当前编辑的多区域列映射", () => {
  assert.match(confirmCardSource, /const activeRegions = computed/);
  assert.match(confirmCardSource, /editableRegions\.value/);
  assert.match(confirmCardSource, /const rangeSummaryFields = computed/);
  assert.match(confirmCardSource, /activeRegions\.value/);
});

test("Excel 数据起始行应由 A1 范围反推且不再单独编辑", () => {
  assert.match(rangeEditorSource, /draft\.dataStartRow = firstRange\.startRow/);
  assert.doesNotMatch(confirmCardSource, /v-model="displayDataStartRowIndex"/);
  assert.doesNotMatch(confirmCardSource, /v-model="state\.headerRowCount"/);
});

test("保存 Excel 范围后应向上查找最近表头并刷新只读标题", () => {
  assert.match(rangeEditorSource, /getTablePreview/);
  assert.match(
    rangeEditorSource,
    /findNearestSmartStructureHeaderRowIndex\([\s\S]*res\.data\.rows/
  );
  assert.match(
    rangeEditorSource,
    /headers: normalizeHeaders\([\s\S]*res\.data\.rows\[matchedRowIndex\]/
  );
  assert.match(rangeEditorSource, /headerRequestVersion/);
});

test("移动端应为固定操作栏预留完整空间并提供至少 44px 触控目标", () => {
  assert.match(
    dataImportStyleSource,
    /--data-import-action-bar-height:\s*\d+px/
  );
  assert.match(
    dataImportStyleSource,
    /padding-bottom:\s*calc\([\s\S]*--data-import-action-bar-height[\s\S]*env\(safe-area-inset-bottom/
  );
  assert.match(
    confirmCardSource,
    /\.card-actions\s+:deep\(\.el-button\)[\s\S]*min-height:\s*44px/
  );
  assert.match(
    confirmCardSource,
    /\.headers-label,[\s\S]*color:\s*var\(--app-text-secondary\)/
  );
});

test("识别失败应保留错误信息并提供重新识别入口", () => {
  assert.match(recognitionComposableSource, /const recognitionError = ref/);
  assert.match(recognitionComposableSource, /recognitionError\.value =/);
  assert.match(summaryBannerSource, /error\?: string/);
  assert.match(summaryBannerSource, /summary\.total > 0 \|\| error/);
  assert.match(dataImportSource, /:error="smartRecognitionError"/);
  assert.match(
    dataImportSource,
    /<DataImportStepUpload[\s\S]*:smart-recognition-error="smartEntryError"[\s\S]*@retry="runSmartStructureRecognition"/
  );
  assert.match(
    uploadStepSource,
    /<SmartStructureSummaryBanner[\s\S]*v-if="smartRecognitionError"/
  );
});

test("登录失效由全局鉴权统一处理，页面初始化请求不应重复弹错", () => {
  assert.match(dataImportTargetSource, /isGloballyHandledAuthError\(error\)/);
  assert.match(
    dataImportTargetSource,
    /catch \(error\)[\s\S]*!isGloballyHandledAuthError\(error\)[\s\S]*加载 AI 服务失败/
  );
});

test("上传目标区域只保留底栏主操作，识别成功后进入确认页", () => {
  assert.doesNotMatch(
    dataImportSource,
    /class="smart-entry-actions"[\s\S]{0,600}@click="goNext"/
  );
  assert.doesNotMatch(
    dataImportSource,
    /class="smart-entry-actions"[\s\S]{0,600}@click="runSmartStructureRecognition"/
  );
  assert.match(
    dataImportSource,
    /class="step-actions"[\s\S]*@click="goNext"[\s\S]*识别并进入确认/
  );
  assert.match(
    dataImportSource,
    /v-if="showManualFallback"[\s\S]*@click="enterAdvancedMode\('tableSelect'\)"[\s\S]*手动处理/
  );
  assert.doesNotMatch(dataImportSource, />\s*高级手动配置\s*</);
});

test("智能识别期间不应挂载隐藏的高级预览，避免同一 Sheet 并发加载", () => {
  assert.match(
    dataImportSource,
    /<DataImportStepMapping\s+v-if="advancedMode && currentStep === 2"/
  );
  assert.doesNotMatch(
    dataImportSource,
    /<DataImportStepMapping\s+v-show="advancedMode && currentStep === 2"/
  );
});

test("两处确认卡片调用均应传递来源文件编号", () => {
  const smartFillSource = readFileSync(
    resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
    "utf8"
  );

  assert.match(
    dataImportSource,
    /<SmartStructureConfirmTabs[\s\S]*:file-id="uploadedFile\?\.fileId"/
  );
  assert.match(
    smartFillSource,
    /<SmartStructureConfirmTabs[\s\S]*:file-id="uploadedFile\?\.fileId"/
  );
});

test("移除高级表单后不再维护无效的默认展开状态", () => {
  assert.doesNotMatch(dataImportSource, /firstNeedConfirmTableIndex/);
  assert.doesNotMatch(dataImportSource, /default-expanded-table-index/);
  assert.doesNotMatch(confirmTabsSource, /defaultExpandedTableIndex/);
});

test("智能确认卡片应提供 Sheet 是否参与导入的勾选入口", () => {
  assert.match(confirmCardSource, /importSelected\?: boolean/);
  assert.match(confirmCardSource, /<el-checkbox/);
  assert.match(confirmTabsSource, /selectedTableIndexes/);
  assert.match(dataImportSource, /selectedSmartTableIndexes/);
  assert.match(dataImportSource, /handleSmartTableImportSelectionChange/);
});

test("数据导入与智能填充应复用同一套识别结果 Tab", () => {
  const smartFillSource = readFileSync(
    resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
    "utf8"
  );

  assert.match(dataImportSource, /<SmartStructureConfirmTabs/);
  assert.match(smartFillSource, /<SmartStructureConfirmTabs/);
  assert.match(confirmTabsSource, /createSmartStructureDisplayGroups/);
});

test("数据导入应隐藏逐 Sheet 确认并只保留一个文件级确认学习导入按钮", () => {
  const primaryActionLabel = "确认所选 Sheet、学习并开始导入";

  assert.match(
    dataImportSource,
    /<SmartStructureConfirmTabs[\s\S]{0,2200}:show-confirm-action="false"/
  );
  assert.match(dataImportSource, new RegExp(primaryActionLabel));
  assert.equal(
    dataImportSource.match(new RegExp(primaryActionLabel, "g"))?.length,
    1,
    "数据导入确认页只能声明一个文件级主操作"
  );
  assert.match(
    dataImportSource,
    /:import-primary-button-text="smartBatchImportButtonText"/
  );
  assert.match(
    dataImportSource,
    /@import="handleSmartStructureBatchConfirmImport"/
  );
});

test("文件级操作应接收每张 Sheet 最终草稿并在批量期间统一锁定交互", () => {
  assert.match(confirmCardSource, /showConfirmAction\?: boolean/);
  assert.match(confirmCardSource, /interactionLocked\?: boolean/);
  assert.match(confirmCardSource, /showConfirmAction: true/);
  assert.match(confirmCardSource, /interactionLocked: false/);
  assert.match(
    confirmCardSource,
    /"draft-change": \[request: SmartConfigConfirmRequest \| null\]/
  );
  assert.match(confirmCardSource, /v-if="showConfirmAction"/);
  assert.match(
    confirmCardSource,
    /props\.readonly\s*\|\|\s*props\.confirmationLocked\s*\|\|\s*props\.interactionLocked/
  );

  assert.match(confirmTabsSource, /showConfirmAction\?: boolean/);
  assert.match(confirmTabsSource, /interactionLocked\?: boolean/);
  assert.match(confirmTabsSource, /:show-confirm-action="showConfirmAction"/);
  assert.match(confirmTabsSource, /:interaction-locked="interactionLocked"/);
  assert.match(
    confirmTabsSource,
    /@draft-change="request => emit\('draft-change', table, request\)"/
  );

  assert.match(
    dataImportSource,
    /@draft-change="handleSmartStructureDraftChange"/
  );
  assert.match(
    dataImportSource,
    /:interaction-locked="[^"]*batchConfirmImportRunning[^"]*"/
  );
  assert.match(dataImportSource, /smartConfirmDrafts/);
});

test("文件级批量确认应展示当前 Sheet 进度并移除旧单 Sheet 分支", () => {
  assert.match(dataImportSource, /batchConfirmProgress/);
  assert.match(dataImportSource, /batchConfirmImportRunning/);
  assert.match(dataImportSource, /runSmartStructureBatchConfirmImportAction/);
  assert.match(dataImportSource, /正在确认第/);
  assert.doesNotMatch(dataImportSource, /combinedConfirmImportTableIndex/);
  assert.doesNotMatch(dataImportSource, /getSingleSelectedTableIndex/);
  assert.doesNotMatch(dataImportSource, /handleSmartStructureConfirmAction/);
  assert.doesNotMatch(confirmTabsSource, /combinedActionTableIndex/);
  assert.doesNotMatch(confirmTabsSource, /combinedActionLabel/);
});

test("智能填充应继续保留默认逐表确认学习操作", () => {
  const smartFillSource = readFileSync(
    resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
    "utf8"
  );

  assert.match(confirmCardSource, /confirmActionLabel: "确认并学习"/);
  assert.match(smartFillSource, /<SmartStructureConfirmTabs/);
  assert.match(smartFillSource, /@confirm="handleSmartStructureConfirm"/);
  assert.doesNotMatch(
    smartFillSource,
    /<SmartStructureConfirmTabs[\s\S]{0,2200}:show-confirm-action="false"/
  );
  assert.doesNotMatch(
    smartFillSource,
    /runSmartStructureBatchConfirmImportAction/
  );
});

test("确认卡应使用完整识别表信息，不应只使用已生成导入配置的 Sheet", () => {
  assert.match(dataImportSource, /:table-infos="smartTableInfos"/);
  assert.doesNotMatch(dataImportSource, /:table-infos="selectedTables"/);
});

test("数据导入与智能填充应向共享识别卡传递真实文件类型和表信息", () => {
  const smartFillSource = readFileSync(
    resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
    "utf8"
  );

  assert.match(dataImportSource, /:table-infos="smartTableInfos"/);
  assert.match(dataImportSource, /:is-excel-file="isExcelFile"/);
  assert.match(smartFillSource, /:table-infos="allTables"/);
  assert.match(smartFillSource, /:is-excel-file="isExcelFile"/);
  assert.match(confirmTabsSource, /:is-excel-file="isExcelFile"/);
});

test("待确认表应可手动勾选，并区分已勾选与已配置汇总", () => {
  assert.match(confirmCardSource, /selectionDisabledReason\?: string/);
  assert.match(confirmCardSource, /selectionPendingReason\?: string/);
  assert.match(confirmCardSource, /暂不可导入/);
  assert.match(confirmCardSource, /已勾选，待配置/);
  assert.match(dataImportSource, /当前表未参与本次导入/);
  assert.match(dataImportSource, /当前表已勾选，仍需配置/);
  assert.match(dataImportSource, /:pending-selected-sheet-count=/);
  assert.match(
    confirmPanelSource,
    /已勾选 \{\{ effectiveSelectedSheetCount \}\} 张 Sheet/
  );
  assert.match(
    confirmPanelSource,
    /已配置合计预计 \{\{ previewDataCount \}\} 条/
  );
  assert.match(
    confirmPanelSource,
    /待导入清单[\s\S]*已配置 Sheet 合计 \{\{ previewDataCount \}\} 条/
  );
  assert.match(
    confirmPanelSource,
    /effectivePendingSelectedSheetCount > 0 \|\|/
  );
});

test("手动处理应携带被点击 Sheet，返回智能确认前同步高级配置", () => {
  assert.match(
    dataImportSource,
    /@advanced="table => enterAdvancedMode\('mapping', table\.tableIndex\)"/
  );
  const pageSource = readFileSync(
    resolve(
      process.cwd(),
      "web/src/views/data-import/composables/useDataImportPage.ts"
    ),
    "utf8"
  );
  assert.match(pageSource, /prepareAdvancedTableConfig\(tableIndex\)/);
  assert.match(pageSource, /syncAdvancedConfigsToRecognizedTables\(\)/);
});

test("任一结构确认进行中应锁定所有结构编辑入口", () => {
  assert.match(confirmCardSource, /const controlsLocked = computed/);
  assert.match(confirmCardSource, /:disabled="controlsLocked"/);
  assert.match(
    confirmCardSource,
    /if \(locked\) rangeEditorVisible\.value = false/
  );
});

test("Reject 未修正时应向辅助技术说明确认按钮禁用原因", () => {
  assert.match(confirmCardSource, /const confirmDisabledReason = computed/);
  assert.match(
    confirmCardSource,
    /decision === "Reject" && !hasStructureChanges\.value/
  );
  assert.match(
    confirmCardSource,
    /:title="confirmDisabledReason \|\| undefined"/
  );
  assert.match(confirmCardSource, /:aria-describedby=/);
  assert.match(confirmCardSource, /class="sr-only"/);
});

test("待确认卡片应展示区域级问题，避免只显示状态而不说明原因", () => {
  assert.match(
    confirmCardSource,
    /activeRegions\.value\.flatMap\(region => region\.issues \?\? \[\]\)/
  );
  assert.match(confirmCardSource, /const visibleIssues = computed/);
  assert.match(confirmCardSource, /v-for="issue in visibleIssues"/);
});

test("范围抽屉保存期间取消后不得提交旧保存请求", () => {
  assert.match(rangeEditorSource, /let saveRequestVersion = 0/);
  assert.match(
    rangeEditorSource,
    /else \{[\s\S]*saveRequestVersion \+= 1;[\s\S]*headerRequestVersion \+= 1;/
  );
  assert.match(
    rangeEditorSource,
    /requestVersion !== saveRequestVersion \|\| !visible\.value/
  );
  const staleGuardIndex = rangeEditorSource.indexOf(
    "requestVersion !== saveRequestVersion"
  );
  const saveEmitIndex = rangeEditorSource.indexOf('emit("save", regions)');
  assert.ok(staleGuardIndex >= 0 && staleGuardIndex < saveEmitIndex);
});

test("Excel 范围抽屉应允许直接编辑 A1 范围并反算内部行列映射", () => {
  assert.match(rangeEditorSource, /直接修改 Excel 范围/);
  assert.match(rangeEditorSource, /v-model="draft\.projectRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.specificationRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.acceptanceRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.remarkRange"/);
  assert.match(rangeEditorSource, /parseExcelA1ColumnRange/);
  assert.match(
    rangeEditorSource,
    /draft\.dataStartRow = firstRange\.startRow;[\s\S]*draft\.dataEndRow = firstRange\.endRow;/
  );
  assert.match(
    rangeEditorSource,
    /relativeColumnIndex = parsed\.columnNumber - baseColumn\.value/
  );
  assert.match(
    rangeEditorSource,
    /draft\.isSpecificationOnly = draft\.projectRange\.trim\(\)\.length === 0/
  );
  assert.doesNotMatch(
    rangeEditorSource,
    /v-model="draft\.projectRange"[\s\S]{0,120}:disabled="draft\.isSpecificationOnly"/
  );
  assert.match(rangeEditorSource, /findNearestSmartStructureHeaderRowIndex/);
  assert.match(
    rangeEditorSource,
    /draft\.headerStartRow = headerRow;[\s\S]*draft\.headerEndRow = headerRow;/
  );
  assert.match(rangeEditorSource, /<template v-else>[\s\S]*表头起始行/);
});

test("智能确认卡应保留 A1 范围并移除重复高级行列表单", () => {
  assert.match(confirmCardSource, /class="range-summary-panel"/);
  assert.match(confirmCardSource, />\s*调整范围\s*</);
  assert.doesNotMatch(confirmCardSource, />高级设置</);
  assert.doesNotMatch(confirmCardSource, /v-model="state\.templateName"/);
  assert.doesNotMatch(confirmCardSource, /v-model="state\.headerRowCount"/);
  assert.doesNotMatch(confirmCardSource, /v-model="displayDataStartRowIndex"/);
  assert.doesNotMatch(
    confirmCardSource,
    /v-model="state\.isSpecificationOnly"/
  );
});

test("高级预览应在写入共享配置前拒绝旧配置响应", () => {
  assert.match(dataImportPageSource, /const previewLoadVersions = new Map/);
  assert.match(dataImportPageSource, /previewConfigFingerprint/);
  assert.match(
    dataImportPageSource,
    /currentFingerprint !== previewConfigFingerprint/
  );
  assert.match(
    dataImportPageSource,
    /ensureCurrentRequest\(\);[\s\S]*cfg\.excelPreviewRowLocations = merged\.rowLocations/
  );
  assert.doesNotMatch(dataImportPageSource, /mapping:\s*any/);
});

test("复制映射到其他工作表应同步识别区域并清理旧预览", () => {
  assert.match(
    dataImportPageSource,
    /pasteMappingConfigToOthers[\s\S]*replaceExcelRegionMapping/
  );
  assert.match(
    dataImportPageSource,
    /cfg\.recognizedExcelMapping = \{ \.\.\.normalizedMapping \}/
  );
  assert.match(
    dataImportPageSource,
    /cfg\.excelPreviewRowLocations = undefined;[\s\S]*cfg\.previewData = null;/
  );
});

test("Word 客户规则和预览失败状态应具备陈旧响应与手动兜底保护", () => {
  const pageSource = readFileSync(
    resolve(
      process.cwd(),
      "web/src/views/data-import/composables/useDataImportPage.ts"
    ),
    "utf8"
  );
  const recognitionSource = readFileSync(
    resolve(
      process.cwd(),
      "web/src/views/data-import/composables/useDataImportSmartStructureRecognition.ts"
    ),
    "utf8"
  );
  assert.match(pageSource, /mappingRulesRequestVersion/);
  assert.match(pageSource, /customerId !== selectedCustomerId\.value/);
  assert.match(recognitionSource, /smartApplyError/);
  assert.match(dataImportSource, /!!smartApplyError\.value/);
});
