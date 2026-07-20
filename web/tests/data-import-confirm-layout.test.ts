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

test("数据导入确认页应把导入设置和待导入清单折叠，避免预览页被明细长表撑散", () => {
  assert.match(confirmPanelSource, /<el-collapse[\s\S]*confirm-panel-collapse/);
  assert.match(confirmPanelSource, /name="duplicate-ai"/);
  assert.match(confirmPanelSource, /name="preview-list"/);
  assert.match(confirmPanelSource, /导入设置/);
  assert.match(confirmPanelSource, /待导入清单/);
});

test("待导入清单不应使用内部纵向滚动，避免鼠标滚轮被表格区域截获", () => {
  assert.doesNotMatch(confirmPanelSource, /max-height="280"/);
});

test("智能结构确认卡片应常驻展示多区域范围，并折叠高级字段", () => {
  assert.match(confirmCardSource, /const detailVisible = ref/);
  assert.match(confirmCardSource, /defaultExpanded\?: boolean/);
  assert.match(confirmCardSource, /class="range-summary-panel"/);
  assert.match(confirmCardSource, /effectiveRowCount/);
  assert.match(confirmCardSource, /ignoredRowCount/);
  assert.match(confirmCardSource, /高级设置|收起高级设置/);
  assert.match(confirmCardSource, /v-show="detailVisible"/);
});

test("智能结构确认卡片应把项目列放在规格列之前", () => {
  const formStart = confirmCardSource.indexOf(
    '<el-form\n      v-show="detailVisible"'
  );
  const formEnd = confirmCardSource.indexOf("</el-form>", formStart);
  const formSource = confirmCardSource.slice(formStart, formEnd);

  const projectIndex = formSource.indexOf('label="项目列"');
  const specificationIndex = formSource.indexOf('label="规格列"');

  assert.ok(formStart >= 0 && formEnd > formStart);
  assert.ok(projectIndex >= 0, "确认表单缺少项目列");
  assert.ok(specificationIndex >= 0, "确认表单缺少规格列");
  assert.ok(projectIndex < specificationIndex, "项目列必须位于规格列之前");
});

test("展开配置后应以当前编辑表单为主，避免重复展示映射和置信度", () => {
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
  assert.match(
    confirmCardSource,
    /projectColumnIndex: state\.projectColumnIndex/
  );
  assert.match(
    confirmCardSource,
    /specificationColumnIndex: state\.specificationColumnIndex/
  );
  assert.match(confirmCardSource, /const rangeSummaryFields = computed/);
  assert.match(confirmCardSource, /activeRegions\.value/);
});

test("调整表头行或表头行数后应同步约束数据起始行", () => {
  assert.match(confirmCardSource, /const minimumDataStartRowIndex = computed/);
  assert.match(
    confirmCardSource,
    /watch\([\s\S]*minimumDataStartRowIndex[\s\S]*state\.dataStartRowIndex/
  );
  assert.match(
    confirmCardSource,
    /v-model="displayDataStartRowIndex"[\s\S]*:min="displayMinimumDataStartRowIndex"/
  );
});

test("调整表头结构后应重新加载对应行并刷新列选项", () => {
  assert.match(confirmCardSource, /getTablePreview/);
  assert.match(
    confirmCardSource,
    /watch\([\s\S]*state\.headerRowIndex[\s\S]*state\.headerRowCount[\s\S]*loadHeadersForCurrentStructure/
  );
  assert.match(confirmCardSource, /const currentHeaders = ref<string\[]>/);
  assert.match(
    confirmCardSource,
    /const columnOptions = computed\([\s\S]*currentHeaders\.value\.map/
  );
  assert.match(confirmCardSource, /headers:\s*\[\.\.\.currentHeaders\.value\]/);
  assert.match(confirmCardSource, /latestHeaderRequestId/);
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

test("数据导入确认页默认只展开第一张待确认表，避免多张表同时展开撑满首屏", () => {
  assert.match(dataImportSource, /firstNeedConfirmTableIndex/);
  assert.match(
    dataImportSource,
    /:default-expanded-table-index="firstNeedConfirmTableIndex"/
  );
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
  assert.match(confirmPanelSource, /待导入清单（已配置 Sheet 合计）/);
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
    /:disabled="[\s\S]*controlsLocked \|\| state\.isSpecificationOnly \|\| headersLoading[\s\S]*"/
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
