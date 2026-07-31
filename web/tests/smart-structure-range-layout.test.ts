import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const confirmCardSource = fs.readFileSync(
  "web/src/views/shared/SmartStructureConfirmCard.vue",
  "utf8"
);
const dataImportSource = fs.readFileSync(
  "web/src/views/data-import/index.vue",
  "utf8"
);
const smartFillSource = fs.readFileSync(
  "web/src/views/smart-fill/index.vue",
  "utf8"
);

test("数据导入与智能填充的多区域列范围应复用共享竖向布局", () => {
  assert.match(dataImportSource, /SmartStructureConfirmTabs/);
  assert.match(smartFillSource, /SmartStructureConfirmTabs/);
  const rangeValuesRule = confirmCardSource.match(
    /\.range-values\s*\{([^}]*)\}/
  )?.[1];
  assert.ok(rangeValuesRule, "共享确认卡片必须定义范围值布局");
  assert.match(rangeValuesRule, /flex-direction:\s*column;/);
});
