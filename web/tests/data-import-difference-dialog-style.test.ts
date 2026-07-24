import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { compileStyle, parse } from "@vue/compiler-sfc";

const componentPath = resolve(
  process.cwd(),
  "web/src/views/data-import/components/DataImportDifferenceConfirmDialog.vue"
);
const shellPath = resolve(
  process.cwd(),
  "web/src/views/data-import/components/DataImportDifferenceDialog.vue"
);

test("差异确认内容样式应由内容组件持有并获得相同的 Vue 作用域", () => {
  const componentSource = readFileSync(componentPath, "utf8");
  const { descriptor } = parse(componentSource, { filename: componentPath });
  const componentStyle = descriptor.styles.find(
    style =>
      style.scoped &&
      style.src === "./DataImportDifferenceConfirmDialog.styles.css"
  );

  assert.ok(
    componentStyle,
    "DataImportDifferenceConfirmDialog.vue 应引用自己的 scoped 样式表"
  );

  const componentStyleSource = componentStyle.src;
  assert.ok(componentStyleSource);

  const stylePath = resolve(componentPath, "..", componentStyleSource);
  const compiled = compileStyle({
    source: readFileSync(stylePath, "utf8"),
    filename: stylePath,
    id: "data-v-difference-dialog-test",
    scoped: true
  });

  assert.equal(compiled.errors.length, 0);
  assert.match(
    compiled.code,
    /\.difference-dialog__summary\[data-v-difference-dialog-test\]/
  );
  assert.match(
    compiled.code,
    /\.difference-card\[data-v-difference-dialog-test\]/
  );
  assert.match(
    compiled.code,
    /\.difference-sheet\[data-v-difference-dialog-test\]/
  );
});

test("差异确认弹窗壳层应限制视口高度并提供独立滚动区域", () => {
  const shellSource = readFileSync(shellPath, "utf8");
  const { descriptor } = parse(shellSource, { filename: shellPath });
  const shellStyle = descriptor.styles.find(
    style => style.scoped && !style.src
  );

  assert.ok(shellStyle, "DataImportDifferenceDialog.vue 应持有弹窗壳层样式");

  const compiled = compileStyle({
    source: shellStyle.content,
    filename: shellPath,
    id: "data-v-difference-shell-test",
    scoped: true
  });

  assert.equal(compiled.errors.length, 0);
  assert.match(compiled.code, /max-height:\s*88vh/);
  assert.match(
    compiled.code,
    /\.difference-dialog\s+\.el-dialog__body[\s\S]*overflow:\s*hidden/
  );
});
