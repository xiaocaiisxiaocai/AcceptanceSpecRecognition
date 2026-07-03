import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const readSource = (path: string) =>
  readFileSync(resolve(process.cwd(), path), "utf8");

const contentLayoutSource = readSource(
  "web/src/layout/components/lay-content/index.vue"
);
const globalStyleSource = readSource("web/src/style/index.scss");
const dataImportStyleSource = readSource(
  "web/src/views/data-import/index.styles.css"
);
const smartFillStyleSource = readSource(
  "web/src/views/smart-fill/index.styles.css"
);

test("主内容容器不应使用 24px 外边距挤占业务首屏", () => {
  assert.doesNotMatch(contentLayoutSource, /\.main-content\s*\{[^}]*margin:\s*24px/s);
});

test("全局页面容器应使用紧凑间距", () => {
  assert.match(globalStyleSource, /\.page\s*\{[^}]*gap:\s*12px/s);
  assert.match(globalStyleSource, /\.page\s*\{[^}]*padding:\s*0/s);
  assert.doesNotMatch(globalStyleSource, /\.page\s*\{[^}]*padding:\s*24px/s);
});

test("流程页顶部和说明区不应保留大段留白", () => {
  for (const source of [dataImportStyleSource, smartFillStyleSource]) {
    assert.doesNotMatch(source, /padding:\s*24px;/);
    assert.doesNotMatch(source, /padding:\s*20px 0;/);
    assert.doesNotMatch(source, /margin-bottom:\s*24px;/);
  }
});
