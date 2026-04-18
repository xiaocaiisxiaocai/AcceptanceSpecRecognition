import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/api/document.ts"),
  "utf8"
);

test("Word 导入接口应显式使用长超时，避免 AI 去重时被默认 10 秒超时中断", () => {
  assert.match(source, /const importRequestTimeout = 300000;/);
  assert.match(
    source,
    /export const importData = \(data: ImportDataRequest\) => \{[\s\S]*timeout:\s*importRequestTimeout[\s\S]*\}/
  );
});

test("Excel 导入接口应显式使用长超时，避免 AI 去重时被默认 10 秒超时中断", () => {
  assert.match(
    source,
    /export const importExcelData = \(data: ExcelImportDataRequest\) => \{[\s\S]*timeout:\s*importRequestTimeout[\s\S]*\}/
  );
});
