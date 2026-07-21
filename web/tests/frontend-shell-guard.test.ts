import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { join, resolve } from "node:path";

const root = resolve(process.cwd(), "web");
const readSource = (path: string) => readFileSync(resolve(root, path), "utf8");

function collectFiles(directory: string, extension: string): string[] {
  return readdirSync(directory).flatMap(name => {
    const path = join(directory, name);
    return statSync(path).isDirectory()
      ? collectFiles(path, extension)
      : path.endsWith(extension)
        ? [path]
        : [];
  });
}

function componentNameFromTag(tag: string) {
  return `El${tag
    .slice(3)
    .split("-")
    .map(part => part[0]?.toUpperCase() + part.slice(1))
    .join("")}`;
}

test("Element Plus 全局注册清单必须与实际模板标签保持一致", () => {
  const usedComponents = new Set<string>();
  for (const path of collectFiles(resolve(root, "src"), ".vue")) {
    const source = readFileSync(path, "utf8");
    for (const match of source.matchAll(/<el-[a-z0-9-]+/g)) {
      usedComponents.add(componentNameFromTag(match[0].slice(1)));
    }
  }

  const pluginSource = readSource("src/plugins/elementPlus.ts");
  const componentList = pluginSource.match(
    /const components = \[([\s\S]*?)\];/
  )?.[1];
  assert.ok(componentList, "Element Plus components 清单不存在");
  const registeredComponents = new Set(
    componentList.match(/El[A-Z][A-Za-z0-9]+/g) ?? []
  );

  assert.deepEqual(
    [...registeredComponents].sort(),
    [...usedComponents].sort(),
    "全局组件清单存在遗漏或未使用项"
  );
  assert.match(pluginSource, /const plugins = \[ElLoading\]/);
  assert.doesNotMatch(
    pluginSource,
    /ElInfiniteScroll|ElPopoverDirective|ElMessageBox|ElNotification/
  );
});

test("ECharts 运行时代码与 CDN 配置应全部移除", () => {
  assert.equal(existsSync(resolve(root, "src/plugins/echarts.ts")), false);
  assert.doesNotMatch(readSource("package.json"), /"echarts"\s*:/);
  assert.doesNotMatch(readSource("build/cdn.ts"), /name:\s*"echarts"/);
  assert.doesNotMatch(readSource("src/main.ts"), /useEcharts|plugins\/echarts/);
});

test("生产构建必须启用有意义的 chunk 提示和 gzip 阻断预算", () => {
  assert.match(readSource("vite.config.ts"), /chunkSizeWarningLimit:\s*1000/);
  assert.match(
    readSource("package.json"),
    /"check:bundle-budget":\s*"node scripts\/assert-bundle-budget\.mjs"/
  );
  assert.match(
    readSource("../.github/workflows/ci.yml"),
    /Enforce frontend gzip budget[\s\S]*pnpm check:bundle-budget/
  );
});

test("核心壳层操作必须使用语义控件、可见焦点和页面标签键盘模型", () => {
  const searchSource = readSource("src/layout/components/lay-search/index.vue");
  const resultSource = readSource(
    "src/layout/components/lay-search/components/SearchResult.vue"
  );
  const tagSource = readSource("src/layout/components/lay-tag/index.vue");
  const tagStyle = readSource("src/layout/components/lay-tag/index.scss");
  const globalStyle = readSource("src/style/index.scss");

  assert.match(searchSource, /<button[\s\S]*aria-label="搜索菜单"/);
  assert.match(resultSource, /<button[\s\S]*class="result-item/);
  assert.match(tagSource, /role="tablist"/);
  assert.match(tagSource, /role="tab"/);
  assert.match(tagSource, /:aria-selected=/);
  assert.match(tagSource, /:tabindex="linkIsActive\(item\) \? 0 : -1"/);
  for (const key of ["ArrowLeft", "ArrowRight", "Home", "End", "Delete"]) {
    assert.match(tagSource, new RegExp(`case "${key}"`));
  }
  assert.match(tagStyle, /min-height:\s*44px/);
  assert.match(globalStyle, /\[role="tab"\]\):focus-visible/);

  for (const path of [
    "src/layout/components/lay-navbar/index.vue",
    "src/layout/components/lay-sidebar/NavHorizontal.vue",
    "src/layout/components/lay-sidebar/NavMix.vue"
  ]) {
    const source = readSource(path);
    assert.match(source, /<button[\s\S]*aria-label="打开系统配置"/);
    assert.match(source, /:alt="`\$\{username \|\| '当前用户'\}头像`"/);
  }
});
