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

test("数据导入与智能填充的每个列范围应显示为起点竖线终点", () => {
  assert.match(dataImportSource, /SmartStructureConfirmTabs/);
  assert.match(smartFillSource, /SmartStructureConfirmTabs/);
  assert.match(
    confirmCardSource,
    /range-boundary[^>]*>\s*\{\{\s*range\.column\s*\}\}\{\{\s*range\.startRow\s*\}\}/
  );
  assert.match(confirmCardSource, /range-connector[^>]*>\s*\|\s*<\/span>/);
  assert.match(
    confirmCardSource,
    /range-boundary[^>]*>\s*\{\{\s*range\.column\s*\}\}\{\{\s*range\.endRow\s*\}\}/
  );
  assert.doesNotMatch(
    confirmCardSource,
    /\{\{\s*range\.startRow\s*\}\}:\{\{\s*range\.column/
  );

  const rangeIntervalRule = confirmCardSource.match(
    /\.range-interval\s*\{([^}]*)\}/
  )?.[1];
  assert.ok(rangeIntervalRule, "每个范围必须定义起点、连接符和终点的布局");
  assert.match(rangeIntervalRule, /flex-direction:\s*column;/);

  const rangeValuesRule = confirmCardSource.match(
    /\.range-values\s*\{([^}]*)\}/
  )?.[1];
  assert.ok(rangeValuesRule, "共享确认卡片必须定义范围值布局");
  assert.match(rangeValuesRule, /flex-direction:\s*column;/);
});
