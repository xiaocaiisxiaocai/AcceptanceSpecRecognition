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
const previewPanelSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/data-import/components/DataImportPreviewPanel.vue"
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
const excelRegionEditorSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/SmartStructureExcelRegionEditor.vue"
  ),
  "utf8"
);
const excelHeaderPreviewSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/smart-structure-region-header-preview.ts"
  ),
  "utf8"
);
const fieldConflictDialogSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/shared/SmartStructureFieldConflictDialog.vue"
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

test("疑似重复提示应移除已处理统计行并提高提示文案行距", () => {
  assert.doesNotMatch(confirmPanelSource, /已完成无重复数据处理/);
  assert.doesNotMatch(confirmPanelSource, /difference-entry__summary/);
  assert.match(
    dataImportStyleSource,
    /\.difference-entry\s+:deep\(\.el-alert__title\)[\s\S]*line-height:\s*1\.6/
  );
  assert.match(
    dataImportStyleSource,
    /\.difference-entry\s+:deep\(\.el-alert__description\)[\s\S]*line-height:\s*1\.7/
  );
});

test("AI 疑似重复检查关闭时应收起无效配置，并由确认组件自身承载样式", () => {
  assert.match(
    confirmPanelSource,
    /v-if="!duplicateAiConfig\.enableSemanticDuplicateCheck"[\s\S]*AI\s*疑似重复检查当前\s*关闭/
  );
  assert.match(
    confirmPanelSource,
    /<el-form[\s\S]*v-else[\s\S]*label-position="top"/
  );
  assert.match(confirmPanelSource, /\.duplicate-ai-panel__mark/);
  assert.doesNotMatch(dataImportStyleSource, /\.duplicate-ai-panel/);
});

test("AI 疑似重复检查应使用业务文案、响应式双列和渐进披露", () => {
  assert.match(confirmPanelSource, /AI 疑似重复检查/);
  assert.match(confirmPanelSource, /命中结果只会进入人工确认，不会自动覆盖/);
  assert.doesNotMatch(confirmPanelSource, />运行中</);
  assert.match(confirmPanelSource, /class="duplicate-ai-advanced-toggle"/);
  assert.match(
    confirmPanelSource,
    /aria-controls="duplicate-ai-advanced-options"/
  );
  assert.match(confirmPanelSource, /<el-col :xs="24" :md="12">/);
  assert.match(confirmPanelSource, /高置信标签阈值/);
  assert.match(confirmPanelSource, /仅控制确认弹窗中的“高置信”标签/);
});

test("待导入清单应使用数量概览和移出语义，避免重复标题与删除歧义", () => {
  assert.match(previewPanelSource, /class="preview-metric primary"/);
  assert.match(previewPanelSource, /选中无关项/);
  assert.match(previewPanelSource, /验收列和备注列同时为空/);
  assert.match(previewPanelSource, /移出所选/);
  assert.match(previewPanelSource, /恢复移出项/);
  assert.doesNotMatch(previewPanelSource, /批量删除/);
  assert.doesNotMatch(previewPanelSource, /待导入数据清单/);
});

test("实时待导入清单应自动补拉完整预览并复用并发请求", () => {
  assert.match(
    previewPanelSource,
    /props\.autoLoadFull,[\s\S]*props\.previewLoadState\.hasPartialPreview,[\s\S]*props\.previewLoadState\.hasPendingInitialPreview/
  );
  assert.match(
    previewPanelSource,
    /if \(!autoLoadFull \|\| !hasPartialPreview \|\| hasPendingInitialPreview\)/
  );
  assert.match(
    previewPanelSource,
    /setTimeout\(\(\) => \{[\s\S]*emit\("loadFullPreview"\)/
  );
  assert.match(
    previewPanelSource,
    /previewLoadState\.hasPartialPreview[\s\S]*irrelevantPreviewRowCount === 0/
  );
  assert.equal(
    dataImportSource.match(/@load-full-preview="ensureFullPreviewDataLoaded"/g)
      ?.length,
    3
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

test("未导入详情只展示行号、原因和四个业务字段", () => {
  assert.match(confirmPanelSource, /class="skipped-rows-table"/);
  assert.match(
    confirmPanelSource,
    /\.skipped-rows-table :deep\(\.el-table__header \.cell\)[\s\S]*white-space:\s*nowrap/
  );
  assert.match(
    dataImportHelpersSource,
    /\{ key: "projectColumn", label: "项目" \}/
  );
  assert.match(
    dataImportHelpersSource,
    /\{ key: "specificationColumn", label: "规格" \}/
  );
  assert.match(
    dataImportHelpersSource,
    /\{ key: "acceptanceColumn", label: "验收" \}/
  );
  assert.match(
    dataImportHelpersSource,
    /\{ key: "remarkColumn", label: "备注" \}/
  );
  assert.doesNotMatch(
    dataImportHelpersSource,
    /buildSkippedPreviewColumns\(headers, maxColumnCount\)/
  );
  assert.match(confirmPanelSource, /mergeSkippedPreviewCellValues/);
});

test("导入完成页应按真实结果展示紧凑摘要，不能把全量跳过称为导入成功", () => {
  assert.match(confirmPanelSource, /const importResultPresentation = computed/);
  assert.match(confirmPanelSource, /title: "本次没有新增数据"/);
  assert.match(confirmPanelSource, /class="result-overview"/);
  assert.match(confirmPanelSource, /class="result-metrics"/);
  assert.doesNotMatch(
    confirmPanelSource,
    /\{\{ importResultPresentation\.description \}\}/
  );
  assert.doesNotMatch(confirmPanelSource, /<el-result/);
  assert.doesNotMatch(confirmPanelSource, /failedCount === 0 \? '导入成功'/);
});

test("跳过明细应按 Sheet 合并区域，无分页展示并在明细区内滚动", () => {
  assert.match(
    confirmPanelSource,
    /class="result-detail result-detail--skipped"/
  );
  assert.doesNotMatch(confirmPanelSource, /<h3>跳过明细<\/h3>/);
  assert.doesNotMatch(confirmPanelSource, />未写入数据库</);
  assert.match(confirmPanelSource, /const skippedSheetGroups = computed/);
  assert.match(confirmPanelSource, /v-model="activeSkippedSheetKey"/);
  assert.match(confirmPanelSource, /v-for="sheet in skippedSheetGroups"/);
  assert.match(
    confirmPanelSource,
    /:name="getSkippedSheetKey\(sheet\.tableIndex\)"/
  );
  assert.match(confirmPanelSource, /class="skipped-tab-label"/);
  assert.match(
    confirmPanelSource,
    /<strong>\{\{ sheet\.dataCount \}\}<\/strong>/
  );
  assert.match(confirmPanelSource, /:data="sheet\.rows"/);
  assert.match(confirmPanelSource, /height="100%"/);
  assert.match(confirmPanelSource, /:row-class-name="getSkippedRowClassName"/);
  assert.match(confirmPanelSource, /:span-method="getSkippedSpanMethod"/);
  assert.match(
    confirmPanelSource,
    /\.skipped-tabs--single :deep\(\.el-tabs__header\)[\s\S]*display:\s*none/
  );
  assert.match(
    confirmPanelSource,
    /\.skipped-region-separator td[\s\S]*background:\s*var\(--el-color-primary-light-9\)/
  );
  assert.match(confirmPanelSource, /区域 \$\{regionNumber\} · 从第/);
  assert.doesNotMatch(confirmPanelSource, /<el-pagination/);
  assert.doesNotMatch(confirmPanelSource, /skippedRowsPageSize/);
  assert.doesNotMatch(confirmPanelSource, /class="skipped-pagination"/);
  assert.doesNotMatch(confirmPanelSource, /返回上传步骤，开始处理下一个文件/);
  assert.doesNotMatch(confirmPanelSource, /max-height="360"/);
  assert.equal(
    confirmPanelSource.match(
      /<el-table-column prop="tableIndex" label="表格" width="80">/g
    )?.length,
    1,
    "表格列只应保留在失败明细，跳过分区不应重复展示"
  );
});

test("导入完成页应解除旧宽度限制并取消无用的底部操作栏占位", () => {
  assert.match(dataImportSource, /'data-import--complete': isCompletionStep/);
  assert.match(
    dataImportSource,
    /<div v-if="!isCompletionStep" class="step-actions">/
  );
  assert.match(
    dataImportStyleSource,
    /\.data-import--complete \.data-import-body\s*\{[\s\S]*?padding-bottom:\s*0;/
  );
  assert.match(
    dataImportStyleSource,
    /\.data-import--complete\s*\{[\s\S]*?height:\s*100%;[\s\S]*?overflow:\s*hidden;/
  );
  assert.match(
    dataImportStyleSource,
    /\.import-result\s*\{[\s\S]*?max-width:\s*none;/
  );
  assert.doesNotMatch(dataImportStyleSource, /max-width:\s*1200px/);
});

test("任一智能导入完成分支都应切换到独立完成步骤", () => {
  assert.match(
    dataImportPageSource,
    /watch\(\s*importResult,\s*result\s*=>\s*\{[\s\S]*!advancedMode\.value[\s\S]*currentStep\.value\s*=\s*SMART_STEP_COMPLETE[\s\S]*\{\s*immediate:\s*true\s*\}/
  );
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

test("智能结构确认卡片的字段范围应在桌面端四列单行展示", () => {
  assert.match(
    confirmCardSource,
    /\.range-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(4,\s*minmax\(0,\s*1fr\)\);/
  );
  assert.doesNotMatch(
    confirmCardSource,
    /\.range-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(2/
  );
});

test("智能结构确认卡片默认应竖向显示完整 A1 范围起止坐标", () => {
  assert.match(
    confirmCardSource,
    /column:\s*formatColumnCoordinate\(columnIndex\)/
  );
  assert.match(
    confirmCardSource,
    /:aria-label="`\$\{range\.column\}\$\{range\.startRow\}:\$\{range\.column\}\$\{range\.endRow\}`"/
  );
  assert.match(confirmCardSource, /class="range-connector"[^>]*>\|<\/span>/);
  assert.equal(confirmCardSource.match(/class="range-boundary"/g)?.length, 2);
  assert.doesNotMatch(confirmCardSource, /class="range-interval-line"/);
  assert.doesNotMatch(confirmCardSource, /field\.columnLabel/);
  assert.doesNotMatch(
    confirmCardSource,
    /<code v-for="range in field\.ranges"/
  );
});

test("确认卡应移除冗余表头标签，并按区域分别展示字段映射", () => {
  assert.doesNotMatch(confirmCardSource, /card-summary-strip/);
  assert.match(confirmCardSource, /const showRecognitionEvidence = computed/);
  assert.doesNotMatch(confirmCardSource, /class="headers-preview"/);
  assert.match(confirmCardSource, /const regionFieldSummaries = computed/);
  assert.match(confirmCardSource, /v-for="region in regionFieldSummaries"/);
  assert.match(confirmCardSource, /region\.label.*表头.*region\.headerRange/s);
});

test("已被当前区域覆盖的表头不应继续显示未覆盖警告", () => {
  assert.match(confirmCardSource, /const isCoveredHeaderIssue/);
  assert.match(confirmCardSource, /rowIndex === region\.dataStartRowIndex - 1/);
  assert.match(
    confirmCardSource,
    /if \(isCoveredHeaderIssue\(issue\)\) return false/
  );
});

test("范围摘要应读取用户当前编辑的多区域列映射", () => {
  assert.match(confirmCardSource, /const activeRegions = computed/);
  assert.match(confirmCardSource, /editableRegions\.value/);
  assert.match(confirmCardSource, /const rangeSummaryFields = computed/);
  assert.match(confirmCardSource, /activeRegions\.value/);
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
    /\.range-label\s*\{[\s\S]*color:\s*var\(--app-text-secondary\)/
  );
});

test("智能确认页应移除识别摘要统计，只保留紧凑的重新识别按钮", () => {
  const smartConfirmStepSource = dataImportSource.slice(
    dataImportSource.indexOf("<!-- 智能流程步骤2: 确认结构与预览 -->"),
    dataImportSource.indexOf("<!-- 智能流程步骤3 / 高级模式步骤5: 完成 -->")
  );

  assert.doesNotMatch(smartConfirmStepSource, /<SmartStructureSummaryBanner/);
  assert.match(
    smartConfirmStepSource,
    /class="smart-recognition-toolbar"[\s\S]*<el-button[\s\S]*:loading="smartRecognizing"[\s\S]*@click="runSmartStructureRecognition"[\s\S]*重新识别[\s\S]*<\/el-button>/
  );
  assert.match(
    dataImportStyleSource,
    /\.smart-confirm-step\s*\{[\s\S]*position:\s*relative/
  );
  assert.match(
    dataImportStyleSource,
    /\.smart-recognition-toolbar\s*\{[\s\S]*position:\s*absolute[\s\S]*justify-content:\s*flex-end/
  );
  assert.match(
    dataImportStyleSource,
    /\.smart-confirm-step\s+:deep\(\.smart-structure-confirm-tabs \.el-tabs__header\)\s*\{[\s\S]*padding-right:/
  );
});

test("识别失败应保留错误信息并提供重新识别入口", () => {
  assert.match(recognitionComposableSource, /const recognitionError = ref/);
  assert.match(recognitionComposableSource, /recognitionError\.value =/);
  assert.match(summaryBannerSource, /error\?: string/);
  assert.match(summaryBannerSource, /summary\.total > 0 \|\| error/);
  assert.match(
    dataImportSource,
    /v-if="smartRecognitionError"[\s\S]*role="alert"[\s\S]*\{\{ smartRecognitionError \}\}/
  );
  assert.match(
    dataImportSource,
    /<DataImportStepUpload[\s\S]*:smart-recognition-error="smartEntryError"[\s\S]*@retry="runSmartStructureRecognition"/
  );
  assert.match(
    uploadStepSource,
    /<SmartStructureSummaryBanner[\s\S]*v-if="smartRecognitionError"/
  );
});

test("登录失效与 AI 状态错误由统一入口处理，页面初始化请求不应重复弹错", () => {
  assert.match(dataImportTargetSource, /isGloballyHandledAuthError\(error\)/);
  const aiLoaderSource = dataImportTargetSource.slice(
    dataImportTargetSource.indexOf("const loadAiServicesOnce"),
    dataImportTargetSource.indexOf("const aiSelectionRetry")
  );
  assert.match(aiLoaderSource, /loadRuntimeAiSelectionsSettled/);
  assert.doesNotMatch(aiLoaderSource, /ElMessage\.(?:error|warning)/);
  assert.match(
    dataImportPageSource,
    /const ensureImportRuntimeAiReady = async[\s\S]*if \(message\) ElMessage\.warning\(message\)/
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
  assert.match(confirmTabsSource, /sortSmartStructureTablesByIndex/);
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
  assert.match(dataImportSource, /:show-import-action="false"/);
  assert.match(
    dataImportSource,
    /class="step-actions"[\s\S]*@click="handleSmartStructureBatchConfirmImport"[\s\S]*\{\{ smartBatchImportButtonText \}\}/
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
  assert.doesNotMatch(dataImportSource, /当前表未参与本次导入/);
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
    /@advanced="\s*table => enterAdvancedMode\('mapping', table\.tableIndex\)\s*"/
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
    /if \(!locked\) return;[\s\S]*rangeEditorVisible\.value = false;[\s\S]*inlineEditorVisible\.value = false;/
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
  assert.match(confirmCardSource, /const structureRecoveryIssues = computed/);
  assert.match(confirmCardSource, /文件结构与历史模板不一致，需要重新确认/);
  assert.match(confirmCardSource, /role="alert"/);
  assert.match(confirmCardSource, /v-for="detail in structureRecoveryDetails"/);
  assert.match(confirmCardSource, /@click="showRangeEditor"/);
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
  assert.match(rangeEditorSource, /v-model="draft\.projectRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.specificationRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.acceptanceRange"/);
  assert.match(rangeEditorSource, /v-model="draft\.remarkRange"/);
  assert.match(rangeEditorSource, /parseExcelA1ColumnRange/);
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

test("Excel 范围抽屉应把每个区域精简为单行四范围编辑器", () => {
  assert.match(rangeEditorSource, /class="excel-region-row"/);
  assert.match(rangeEditorSource, /class="excel-region-index"/);
  assert.match(rangeEditorSource, /class="excel-region-fields"/);
  assert.match(
    rangeEditorSource,
    /grid-template-columns:\s*28px repeat\(4,\s*minmax\(0,\s*1fr\)\) auto/
  );
  assert.match(rangeEditorSource, /aria-label="项目范围"/);
  assert.match(rangeEditorSource, /aria-label="规格范围"/);
  assert.match(rangeEditorSource, /aria-label="验收范围"/);
  assert.match(rangeEditorSource, /aria-label="备注范围"/);
  assert.doesNotMatch(rangeEditorSource, /class="excel-range-context"/);
});

test("Excel 范围抽屉应隐藏说明并压缩顶部标题栏", () => {
  assert.match(
    rangeEditorSource,
    /v-if="!isExcelFile"\s+class="range-editor-intro"/
  );
  assert.match(rangeEditorSource, /class="smart-structure-range-drawer"/);
  assert.match(
    rangeEditorSource,
    /smart-structure-range-drawer \.el-drawer__header[\s\S]*margin-bottom:\s*0/
  );
  assert.match(
    rangeEditorSource,
    /smart-structure-range-drawer \.el-drawer__body[\s\S]*padding-top:\s*8px/
  );
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

test("数据导入 Excel 确认页应启用同一草稿的内联行列与 A1 编辑器", () => {
  assert.match(dataImportSource, /:inline-excel-region-editor="isExcelFile"/);
  assert.match(
    dataImportSource,
    /applySmartConfigConfirmRequestToTable\(table, draft\)[\s\S]*getSmartStructureImportReadinessReason\(effectiveTable\)/
  );
  assert.match(
    dataImportSource,
    /const activeSmartStructureReadinessReason = computed\(\(\) => \{[\s\S]*smartConfirmDrafts\.value\[table\.tableIndex\][\s\S]*applySmartConfigConfirmRequestToTable\(table, draft\)[\s\S]*getSmartStructureImportReadinessReason\(effectiveTable\)/
  );
  assert.match(confirmTabsSource, /inlineExcelRegionEditor\?: boolean/);
  assert.match(
    confirmTabsSource,
    /:inline-excel-region-editor="inlineExcelRegionEditor"/
  );
  assert.match(confirmCardSource, /import SmartStructureExcelRegionEditor/);
  assert.match(
    confirmCardSource,
    /useInlineExcelEditor\.value[\s\S]*normalizeSmartStructureInlineExcelRegion\(region\)/
  );
  assert.match(
    confirmCardSource,
    /const requestRegions = computed\(\(\) =>[\s\S]*activeRegions\.value\.map\(normalizeRegionForEditor\)/
  );
  assert.match(
    confirmCardSource,
    /getRecognizedRegions\(\)[\s\S]*\.map\(normalizeRegionForEditor\)[\s\S]*\.map\(compactRegionStructure\)/
  );
  assert.match(
    confirmCardSource,
    /v-if="useInlineExcelEditor"[\s\S]*:model-value="activeRegions"/
  );
  assert.match(confirmCardSource, /<Teleport to="body">/);
  assert.match(
    confirmCardSource,
    /v-show="inlineEditorVisible"[\s\S]*role="dialog"[\s\S]*SmartStructureExcelRegionEditor/
  );
  assert.match(confirmCardSource, /inlineEditorVisible\.value = false/);
  assert.doesNotMatch(
    confirmCardSource,
    /inlineEditorVisible\.value = !inlineEditorVisible\.value/
  );
  assert.match(confirmCardSource, /@update:model-value="handleRangesSave"/);
  assert.match(
    confirmCardSource,
    /!allRegionsConfirmable\.value \|\|[\s\S]*structureValidationError\.value/
  );
  assert.match(
    confirmCardSource,
    /hasStructureChanges,[\s\S]*structureValidationError[\s\S]*emit\("draft-change", buildDraftRequest\(\)\)/
  );
  assert.match(
    confirmCardSource,
    /<SmartStructureRangeEditorDrawer[\s\S]*v-if="!useInlineExcelEditor"/
  );
});

test("Excel 单表头归一化必须计入结构变更，避免 AutoApply 跳过新草稿", () => {
  assert.match(
    confirmCardSource,
    /const originalRegionStructures = computed\(\(\) =>[\s\S]*getRecognizedRegions\(\)[\s\S]*\.map\(compactRegionStructure\)/
  );
  const originalStructuresStart = confirmCardSource.indexOf(
    "const originalRegionStructures"
  );
  const changesStart = confirmCardSource.indexOf(
    "const regionsHaveChanges",
    originalStructuresStart
  );
  const originalStructuresSource = confirmCardSource.slice(
    originalStructuresStart,
    changesStart
  );
  assert.doesNotMatch(originalStructuresSource, /normalizeRegionForEditor/);
  assert.match(
    dataImportSource,
    /table\.decision !== "AutoApply" \|\|[\s\S]*Boolean\(request\?\.userModifiedStructure\)/
  );
});

test("右侧抽屉应圈定焦点、恢复触发按钮并锁定背景滚动", () => {
  assert.match(confirmCardSource, /onBeforeUnmount/);
  assert.match(confirmCardSource, /const handleInlineDrawerKeydown/);
  assert.match(confirmCardSource, /event\.key !== "Tab"/);
  assert.match(confirmCardSource, /inlineDrawerTriggerRef/);
  assert.match(confirmCardSource, /document\.body\.style\.overflow = "hidden"/);
  assert.match(confirmCardSource, /restoreInlineDrawerEnvironment/);
  assert.match(confirmCardSource, /@keydown="handleInlineDrawerKeydown"/);
  assert.match(confirmCardSource, /@touchmove\.prevent/);
  assert.match(
    confirmCardSource,
    /resetState\(\);[\s\S]*inlineEditorVisible\.value = true;[\s\S]*await nextTick\(\);[\s\S]*inlineDrawerRef\.value\?\.focus\(\)/
  );
  assert.match(
    confirmCardSource,
    /\.smart-structure-inline-drawer__body\s*\{[\s\S]*overscroll-behavior:\s*contain/
  );
});

test("内联 Excel 区域编辑器只编辑数据行，并展示列映射、A1 与显式仅规格开关", () => {
  assert.doesNotMatch(excelRegionEditorSource, />表头起始行</);
  assert.doesNotMatch(excelRegionEditorSource, />表头行数</);
  assert.match(excelRegionEditorSource, />数据起始行</);
  assert.match(excelRegionEditorSource, />数据结束行</);
  assert.match(excelRegionEditorSource, /表头固定取数据起始行的上一行/);
  assert.match(
    excelRegionEditorSource,
    /field: "project"[\s\S]{0,80}label: "项目"/
  );
  assert.match(
    excelRegionEditorSource,
    /field: "specification"[\s\S]{0,80}label: "规格"/
  );
  assert.match(
    excelRegionEditorSource,
    /field: "acceptance"[\s\S]{0,80}label: "验收"/
  );
  assert.match(
    excelRegionEditorSource,
    /field: "remark"[\s\S]{0,80}label: "备注"[\s\S]{0,100}required: true/
  );
  assert.match(
    excelRegionEditorSource,
    /:aria-label="`\$\{definition\.label\}起始单元格`"[\s\S]*:aria-label="`\$\{definition\.label\}结束单元格`"/
  );
  assert.match(excelRegionEditorSource, /仅规格表（没有独立项目列）/);
  assert.match(excelRegionEditorSource, /setSmartStructureSpecificationOnly/);
  assert.match(excelRegionEditorSource, /smart-structure-column-select-popper/);
  assert.match(
    excelRegionEditorSource,
    /<span>数据起始行<\/span>[\s\S]*:min="rowInputLimits\(draft\)\.dataStartMinimum"[\s\S]*:max="rowInputLimits\(draft\)\.dataStartMaximum"/
  );
  assert.match(
    excelRegionEditorSource,
    /<span>数据结束行<\/span>[\s\S]*:min="rowInputLimits\(draft\)\.dataEndMinimum"[\s\S]*:max="rowInputLimits\(draft\)\.dataEndMaximum"/
  );
});

test("Excel 范围抽屉默认按四列两行显示端点和起止单元格内容，其他配置展开后才可使用", () => {
  assert.doesNotMatch(excelRegionEditorSource, />字段范围</);
  assert.doesNotMatch(
    excelRegionEditorSource,
    /分别填写项目、规格、验收和备注的起止单元格/
  );
  assert.match(excelRegionEditorSource, /const detailsExpanded = ref\(false\)/);
  assert.match(excelRegionEditorSource, /drawerOpen\?: boolean/);
  assert.match(
    excelRegionEditorSource,
    /watch\([\s\S]*\(\) => props\.drawerOpen[\s\S]*detailsExpanded\.value = false/
  );
  const resetVersionAccessorStart = excelRegionEditorSource.indexOf(
    "() => props.resetVersion"
  );
  const resetVersionWatchStart = excelRegionEditorSource.lastIndexOf(
    "watch(",
    resetVersionAccessorStart
  );
  const drawerOpenWatchStart = excelRegionEditorSource.indexOf(
    "() => props.drawerOpen",
    resetVersionWatchStart
  );
  const resetVersionWatchSource = excelRegionEditorSource.slice(
    resetVersionWatchStart,
    drawerOpenWatchStart
  );
  assert.match(resetVersionWatchSource, /detailsExpanded\.value = false/);
  assert.match(resetVersionWatchSource, /resetFromModel\(\)/);
  assert.match(
    excelRegionEditorSource,
    /:aria-expanded="detailsExpanded"[\s\S]*展开更多配置/
  );
  assert.match(excelRegionEditorSource, /:aria-controls="detailsControlIds"/);
  assert.match(excelRegionEditorSource, /const detailsControlIds = computed\(/);
  assert.match(excelRegionEditorSource, /details-toolbar/);
  assert.match(excelRegionEditorSource, /details-region/);
  assert.doesNotMatch(excelRegionEditorSource, /details-add/);
  assert.match(
    confirmCardSource,
    /<SmartStructureExcelRegionEditor[\s\S]*:drawer-open="inlineEditorVisible"/
  );
  assert.match(
    excelRegionEditorSource,
    /<span\s+v-if="detailsExpanded"\s+class="excel-region-card__rows"/
  );

  const a1GridStart = excelRegionEditorSource.indexOf(
    'class="excel-region-a1-grid"'
  );
  const detailsStart = excelRegionEditorSource.indexOf(
    'class="excel-region-details"'
  );
  assert.notEqual(a1GridStart, -1);
  assert.notEqual(detailsStart, -1);
  assert.ok(a1GridStart < detailsStart);

  const compactA1Source = excelRegionEditorSource.slice(
    a1GridStart,
    detailsStart
  );
  assert.match(
    compactA1Source,
    /v-for="definition in fieldDefinitions"[\s\S]*class="excel-region-a1-field"[\s\S]*class="excel-region-endpoint-row is-start"[\s\S]*class="excel-region-start-cell-value"[\s\S]*class="excel-region-endpoint-row is-end"/
  );
  assert.doesNotMatch(compactA1Source, /\{\{ definition\.label \}\}范围/);
  assert.match(
    compactA1Source,
    /:placeholder="getA1EndpointPlaceholder\(definition, 'start'\)"[\s\S]*:placeholder="getA1EndpointPlaceholder\(definition, 'end'\)"/
  );
  assert.match(
    compactA1Source,
    /a1EndpointBuffers\[draft\.regionId\]\?\.\[definition\.field\]\?\.start[\s\S]*handleA1EndpointInput\([\s\S]*'start'[\s\S]*a1EndpointBuffers\[draft\.regionId\]\?\.\[definition\.field\]\?\.end[\s\S]*handleA1EndpointInput\([\s\S]*'end'/
  );
  assert.match(
    compactA1Source,
    /getStartCellContent\(draft, definition\.field\)/
  );
  assert.match(
    compactA1Source,
    /class="excel-region-endpoint-row is-end"[\s\S]*class="excel-region-end-cell-value"[\s\S]*getEndCellContent\(draft, definition\.field\)/
  );
  assert.match(
    excelRegionEditorSource,
    /const handleColumnChange[\s\S]*getColumnValue\(next, field\) !== currentColumn[\s\S]*scheduleHeaderLoad\(next\.regionId\)/
  );
  assert.match(
    excelRegionEditorSource,
    /const handleA1EndpointInput[\s\S]*getColumnValue\(result\.draft, field\) !== previousColumn[\s\S]*scheduleHeaderLoad\(result\.draft\.regionId\)/
  );
  assert.match(
    excelRegionEditorSource,
    /result\.draft\.dataEndRow !== draft\.dataEndRow/
  );
  assert.match(
    excelRegionEditorSource,
    /field === "dataStartRow" \|\| field === "dataEndRow"[\s\S]*scheduleHeaderLoad\(next\.regionId\)/
  );
  assert.match(
    compactA1Source,
    /:aria-errormessage="[\s\S]*getA1EndpointErrorId\([\s\S]*'start'[\s\S]*getA1EndpointErrorId\([\s\S]*'end'/
  );
  assert.doesNotMatch(compactA1Source, /<el-select/);
  assert.doesNotMatch(compactA1Source, />数据起始行</);
  assert.match(
    excelRegionEditorSource,
    /\.excel-region-a1-grid\s*\{[\s\S]*?grid-template-columns:\s*repeat\(4,\s*minmax\(0,\s*1fr\)\)/
  );
  assert.match(
    excelRegionEditorSource,
    /\.excel-region-endpoint-row\.is-start\s*\{[\s\S]*grid-template-columns:\s*minmax\([^;]+;/
  );
  assert.match(
    excelRegionEditorSource,
    /\.excel-region-endpoint-row\.is-end\s*\{[\s\S]*grid-template-columns:\s*minmax\([^;]+;/
  );
  assert.match(
    excelRegionEditorSource,
    /\.excel-region-start-cell-value,\s*\.excel-region-end-cell-value\s*\{[\s\S]*text-overflow:\s*ellipsis/
  );
  assert.match(
    excelRegionEditorSource,
    /class="excel-region-editor__summary-actions"[\s\S]*@click="addRegion"[\s\S]*添加数据区域[\s\S]*:aria-expanded="detailsExpanded"/
  );

  const detailsSource = excelRegionEditorSource.slice(detailsStart);
  assert.match(detailsSource, />数据起始行</);
  assert.match(detailsSource, />数据结束行</);
  assert.match(detailsSource, /<el-select/);
  assert.match(detailsSource, /仅规格表（没有独立项目列）/);
  assert.match(detailsSource, /复制[\s\S]*删除/);
  assert.doesNotMatch(detailsSource, /添加数据区域/);
});

test("内联 Excel 编辑器应按数据起始行上一行刷新单行表头且不向上搜索", () => {
  assert.match(
    excelRegionEditorSource,
    /createSmartStructureHeaderPreviewLoader/
  );
  assert.match(excelRegionEditorSource, /scheduleHeaderLoad/);
  const validationStart = excelRegionEditorSource.indexOf(
    "const firstValidationError"
  );
  const validationEnd = excelRegionEditorSource.indexOf(
    "watch(firstValidationError"
  );
  assert.ok(validationStart >= 0 && validationEnd > validationStart);
  const blockingValidationSource = excelRegionEditorSource.slice(
    validationStart,
    validationEnd
  );
  assert.match(
    blockingValidationSource,
    /resolveSmartStructureExcelBlockingValidationError/
  );
  assert.doesNotMatch(
    blockingValidationSource,
    /headerLoading|headerErrors/,
    "端点内容预览属于只读提示，加载中或读取失败都不应阻断确认"
  );
  assert.match(
    excelRegionEditorSource,
    /field === "dataStartRow"[\s\S]*scheduleHeaderLoad\(next\.regionId\)/
  );
  assert.match(
    excelRegionEditorSource,
    /refreshA1EndpointBuffers\(drafts\.value\);[\s\S]*scheduleHeaderLoad\(draft\.regionId\)/
  );
  assert.match(
    excelRegionEditorSource,
    /result\.draft\.dataStartRow !== draft\.dataStartRow[\s\S]*scheduleHeaderLoad\(result\.draft\.regionId\)/
  );
  assert.match(
    excelHeaderPreviewSource,
    /headerRowIndex:\s*input\.dataStartRow - input\.baseRow - 1,[\s\S]*headerRowCount:\s*1/
  );
  assert.doesNotMatch(
    excelRegionEditorSource,
    /findNearestSmartStructureHeaderRowIndex/
  );
  assert.doesNotMatch(excelRegionEditorSource, /searchEndRow/);
});

test("数据导入与智能填充 Excel 确认页应复用同一内联区域编辑器", () => {
  const smartFillSource = readFileSync(
    resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
    "utf8"
  );

  assert.match(
    confirmCardSource,
    /useInlineExcelEditor\.value[\s\S]*normalizeSmartStructureInlineExcelRegion/
  );
  assert.match(smartFillSource, /:inline-excel-region-editor="isExcelFile"/);
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

test("数据导入应在批量学习前确认已选 Sheet 的字段候选冲突", () => {
  assert.match(
    dataImportSource,
    /collectSmartStructureFieldConflicts\(\s*recognizedTables\.value,\s*selectedSmartTableIndexes\.value\s*\)/
  );
  assert.match(
    dataImportSource,
    /if \(conflicts\.length > 0\) \{[\s\S]*fieldConflictDialogVisible\.value = true;[\s\S]*return;/
  );
  assert.match(
    dataImportSource,
    /applySmartStructureFieldSelectionsToTable\(table, selections\)/
  );
  assert.match(
    dataImportSource,
    /applySmartStructureFieldSelectionsToDraft\(\s*request,\s*table,\s*selections\s*\)/
  );
  assert.match(
    dataImportSource,
    /replaceRecognizedTables\(nextTables, uploadedFile\.value\?\.fileId\)/
  );
  assert.match(
    dataImportSource,
    /await nextTick\(\);[\s\S]*await executeSmartStructureBatchConfirmImport\(\);/
  );
  assert.match(
    dataImportSource,
    /const handleFieldConflictCancel = \(\) => \{[\s\S]*fieldConflictDialogVisible\.value = false;[\s\S]*pendingFieldConflicts\.value = \[\];/
  );
  assert.match(
    dataImportSource,
    /<SmartStructureFieldConflictDialog[\s\S]*:conflicts="pendingFieldConflicts"[\s\S]*@cancel="handleFieldConflictCancel"[\s\S]*@confirm="handleFieldConflictConfirm"/
  );
});

test("数据导入字段候选冲突应在正式预览生成前处理", () => {
  assert.match(
    dataImportSource,
    /resolveInitialFieldConflicts:[\s\S]*resolveDataImportFieldConflicts/
  );
  assert.match(
    dataImportSource,
    /dataImportFieldConflictContext\.value = "initial"/
  );
  assert.match(
    dataImportSource,
    /handleFieldConflictConfirm[\s\S]*const context = dataImportFieldConflictContext\.value;[\s\S]*if \(context === "initial"\)[\s\S]*finishPendingInitialFieldConflict\(nextTables\)/
  );
  assert.match(
    dataImportSource,
    /onBeforeUnmount\(\(\) => \{[\s\S]*finishPendingInitialFieldConflict\(null\);[\s\S]*\}\);/
  );
});

test("暂不处理字段候选冲突后应继续生成预览并允许重新选择", () => {
  assert.match(
    dataImportSource,
    /const handleFieldConflictCancel = \(\) => \{[\s\S]*dataImportFieldConflictContext\.value === "initial"[\s\S]*finishPendingInitialFieldConflict\(\s*pendingInitialFieldConflictTables\s*\)/
  );
  assert.match(
    dataImportSource,
    /const unresolvedFieldConflicts = computed\([\s\S]*collectSmartStructureFieldConflicts\([\s\S]*recognizedTables\.value,[\s\S]*selectedSmartTableIndexes\.value/
  );
  assert.match(
    dataImportSource,
    /const reopenFieldConflictDialog = \(\) => \{[\s\S]*dataImportFieldConflictContext\.value = "preview";[\s\S]*fieldConflictDialogVisible\.value = true;/
  );
  assert.match(
    dataImportSource,
    /v-if="unresolvedFieldConflicts\.length > 0"[\s\S]*@click="reopenFieldConflictDialog"[\s\S]*重新选择数据列/
  );
  assert.match(
    dataImportSource,
    /if \(context === "preview"\)[\s\S]*previewSmartRecognizedTables\(nextTables\)/
  );
});

test("字段候选弹框应预选推荐项并明确提示仍需人工确认", () => {
  assert.match(
    fieldConflictDialogSource,
    /hasAdjustedSelection \? "已选择" : "已预选"/
  );
  assert.match(fieldConflictDialogSource, /class="selection-status"/);
  assert.match(fieldConflictDialogSource, /<h3>确认数据列<\/h3>/);
  assert.doesNotMatch(fieldConflictDialogSource, /<el-progress/);
  assert.doesNotMatch(fieldConflictDialogSource, /class="conflict-progress"/);
  assert.match(
    fieldConflictDialogSource,
    /@change="hasAdjustedSelection = true"/
  );
  assert.doesNotMatch(fieldConflictDialogSource, /已完成/);
});
