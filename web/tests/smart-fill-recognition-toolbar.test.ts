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
