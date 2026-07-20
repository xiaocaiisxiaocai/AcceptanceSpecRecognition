import fs from "node:fs/promises";
import { FileBlob, SpreadsheetFile } from "@oai/artifact-tool";

const inputPath = "C:/Users/SAC/Desktop/验收规范/苏州群策/翻板机验收规范-SAC20260514.xlsx";
const input = await FileBlob.load(inputPath);
const workbook = await SpreadsheetFile.importXlsx(input);
const sheets = await workbook.inspect({ kind: "sheet", include: "id,name", maxChars: 2000 });
console.log(sheets.ndjson);
const sheet = workbook.worksheets.getItemAt(0);
const rows = await workbook.inspect({
  kind: "table",
  sheetId: sheet.name,
  range: "A120:L148",
  include: "values,formulas",
  tableMaxRows: 40,
  tableMaxCols: 12,
  tableMaxCellChars: 160,
  maxChars: 16000,
});
console.log(rows.ndjson);
const region = await workbook.inspect({
  kind: "region",
  sheetId: sheet.name,
  range: "A120:L148",
  maxChars: 5000,
});
console.log(region.ndjson);
const preview = await workbook.render({
  sheetName: sheet.name,
  range: "A120:L148",
  scale: 1.5,
  format: "png",
});
await fs.writeFile(
  "D:/Temp/AcceptanceSpecificationSystem/.codex/sheet-inspect/rows-120-148.png",
  new Uint8Array(await preview.arrayBuffer()),
);
