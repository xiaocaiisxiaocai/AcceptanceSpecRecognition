import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

const smartFillSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
  "utf8"
);
const smartFillStyleSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/index.styles.css"),
  "utf8"
);
const smartFillStructurePreviewSource = readFileSync(
  resolve(
    process.cwd(),
    "web/src/views/smart-fill/components/SmartFillStructurePreviewPanel.vue"
  ),
  "utf8"
);

const reviewStart = smartFillSource.indexOf(
  'class="step-panel smart-fill-recognition-review"'
);
const tabsStart = smartFillSource.indexOf(
  "<SmartStructureConfirmTabs",
  reviewStart
);
const reviewToolbarSource = smartFillSource.slice(reviewStart, tabsStart);

test("智能填充识别确认页移除结果概览并将重新识别移到页签栏", () => {
  assert.ok(reviewStart >= 0, "应找到智能填充识别确认页");
  assert.ok(tabsStart > reviewStart, "应找到识别结果 Tab");
  assert.doesNotMatch(smartFillSource, /selectedScopeSummary/);
  assert.doesNotMatch(reviewToolbarSource, /smart-fill-recognition-context/);
  assert.doesNotMatch(reviewToolbarSource, /<SmartStructureSummaryBanner/);
  assert.match(
    reviewToolbarSource,
    /smart-fill-recognition-toolbar[\s\S]*:loading="smartRecognizing"[\s\S]*@click="runSmartStructureRecognition"[\s\S]*重新识别/
  );
  assert.match(
    smartFillStyleSource,
    /\.smart-fill-recognition-toolbar\s*{[\s\S]*position:\s*absolute;[\s\S]*right:\s*0;/
  );
  assert.match(
    smartFillStyleSource,
    /\.smart-fill-recognition-review[\s\S]*\.smart-structure-confirm-tabs \.el-tabs__header[\s\S]*padding-right:/
  );
});

test("智能填充识别确认页使用双栏待填充预览并在窄屏上下排列", () => {
  assert.match(
    smartFillSource,
    /class="smart-fill-recognition-workspace"[\s\S]*class="smart-fill-recognition-workspace__configuration"[\s\S]*<SmartStructureConfirmTabs[\s\S]*class="smart-fill-recognition-workspace__preview"[\s\S]*<SmartFillStructurePreviewPanel/
  );
  assert.match(
    smartFillSource,
    /:config="activeSmartFillStructurePreviewConfig"/
  );
  assert.match(
    smartFillStyleSource,
    /\.smart-fill-recognition-workspace\s*{[\s\S]*display:\s*grid;[\s\S]*grid-template-columns:/
  );
  assert.match(
    smartFillStyleSource,
    /@media\s*\(width\s*<=\s*1280px\)[\s\S]*\.smart-fill-recognition-workspace\s*{[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\);/
  );
  assert.match(
    smartFillStructurePreviewSource,
    /v-if="regions\.length > 1"[\s\S]*v-model="activeRegionKey"[\s\S]*<TablePreview[\s\S]*activeRegion\.mapping/
  );
  assert.match(
    smartFillStructurePreviewSource,
    /<TablePreview[\s\S]*:fit-columns="true"/
  );
  assert.match(
    smartFillStructurePreviewSource,
    /:mapped-column-widths="\[140, undefined, 112, 112\]"/
  );
  assert.match(
    smartFillStructurePreviewSource,
    /:preview-rows="activeRegion\.previewRows"/
  );
  assert.match(
    smartFillStructurePreviewSource,
    /:row-number-start="activeRegion\.sourceRowNumberStart"/
  );
});
