import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const tableSelectorSource = readFileSync(
  resolve(process.cwd(), "web/src/views/data-import/components/TableSelector.vue"),
  "utf8"
);

test("多表格选择器应支持按名称和表头筛选", () => {
  assert.match(tableSelectorSource, /const searchKeyword = ref\(""\)/);
  assert.match(tableSelectorSource, /const filteredTables = computed/);
  assert.match(tableSelectorSource, /v-model="searchKeyword"/);
  assert.match(tableSelectorSource, /v-for="table in filteredTables"/);
});

test("多表格选择器应支持紧凑模式，适配大量 Sheet", () => {
  assert.match(tableSelectorSource, /const compactMode = ref\(false\)/);
  assert.match(tableSelectorSource, /compactMode/);
  assert.match(tableSelectorSource, /class="table-list-actions"/);
});
