import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import test from "node:test";
import assert from "node:assert/strict";

const smartFillSource = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/index.vue"),
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

test("智能填充识别确认页只在标题栏保留重新识别入口", () => {
  assert.ok(reviewStart >= 0, "应找到智能填充识别确认页");
  assert.ok(tabsStart > reviewStart, "应找到识别结果 Tab");
  assert.doesNotMatch(reviewToolbarSource, /<SmartStructureSummaryBanner/);
  assert.match(
    reviewToolbarSource,
    /smart-fill-recognition-context__retry[\s\S]*:loading="smartRecognizing"[\s\S]*@click="runSmartStructureRecognition"[\s\S]*重新识别/
  );
});
