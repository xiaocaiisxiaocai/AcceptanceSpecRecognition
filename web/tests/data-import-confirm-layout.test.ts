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

test("智能结构确认卡片应支持默认折叠摘要态，减少多表确认时的纵向噪音", () => {
  assert.match(confirmCardSource, /const detailVisible = ref/);
  assert.match(confirmCardSource, /defaultExpanded\?: boolean/);
  assert.match(confirmCardSource, /class="card-summary-strip"/);
  assert.match(confirmCardSource, /展开配置|收起配置/);
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
  const summaryClassIndex = confirmCardSource.indexOf(
    'class="card-summary-strip"'
  );
  const summaryTagStart = confirmCardSource.lastIndexOf(
    "<div",
    summaryClassIndex
  );
  const summaryTagEnd = confirmCardSource.indexOf(">", summaryClassIndex);
  const summaryTag = confirmCardSource.slice(summaryTagStart, summaryTagEnd);

  assert.ok(summaryClassIndex >= 0, "缺少折叠摘要区域");
  assert.match(summaryTag, /v-show="!detailVisible"/);
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

test("折叠摘要应读取用户当前编辑的列映射", () => {
  assert.match(confirmCardSource, /getHeaderText\(state\.projectColumnIndex\)/);
  assert.match(
    confirmCardSource,
    /getHeaderText\(state\.specificationColumnIndex\)/
  );
  assert.match(
    confirmCardSource,
    /getHeaderText\(state\.acceptanceColumnIndex\)/
  );
  assert.match(confirmCardSource, /getHeaderText\(state\.remarkColumnIndex\)/);
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
    /<DataImportStepUpload[\s\S]*:smart-recognition-error="smartRecognitionError"[\s\S]*@retry="runSmartStructureRecognition"/
  );
  assert.match(
    uploadStepSource,
    /<SmartStructureSummaryBanner[\s\S]*v-if="smartRecognitionError"/
  );
});

test("上传目标区域的智能识别入口应复用下一步流程，识别成功后进入确认页", () => {
  assert.match(
    dataImportSource,
    /class="smart-entry-actions"[\s\S]*@click="goNext"[\s\S]*智能识别结构/
  );
  assert.doesNotMatch(
    dataImportSource,
    /class="smart-entry-actions"[\s\S]*@click="runSmartStructureRecognition"[\s\S]*智能识别结构/
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
