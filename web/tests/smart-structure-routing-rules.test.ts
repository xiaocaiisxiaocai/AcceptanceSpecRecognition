import test from "node:test";
import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

const getRepositoryRoot = () => {
  const cwd = process.cwd();
  if (existsSync(resolve(cwd, "web/package.json"))) {
    return cwd;
  }

  const parent = resolve(cwd, "..");
  if (existsSync(resolve(parent, "web/package.json"))) {
    return parent;
  }

  return cwd;
};

const repositoryRoot = getRepositoryRoot();
const readProjectFile = (relativePath: string) =>
  readFileSync(resolve(repositoryRoot, relativePath), "utf8");

test("前端应开放智能结构路由规则配置入口", () => {
  const apiPath = resolve(repositoryRoot, "web/src/api/smart-structure-routing-rules.ts");
  const pagePath = resolve(repositoryRoot, "web/src/views/config/smart-structure-routing-rules/index.vue");
  const configRouteSource = readProjectFile("web/src/router/modules/config.ts");
  const navigationManifestSource = readProjectFile(
    "shared/navigation/navigation-manifest.json"
  );

  assert.equal(existsSync(apiPath), true);
  assert.equal(existsSync(pagePath), true);
  assert.match(configRouteSource, /\/config\/smart-structure-routing-rules/);
  assert.match(configRouteSource, /SmartStructureRoutingRules/);
  assert.match(navigationManifestSource, /config-smart-structure-routing-rules/);
  assert.match(navigationManifestSource, /page:config:smart-structure-routing-rules/);
});

test("智能结构路由规则页应支持人工规则与学习规则维护", () => {
  const source = readProjectFile(
    "web/src/views/config/smart-structure-routing-rules/index.vue"
  );

  assert.match(source, /value:\s*"Manual"/);
  assert.match(source, /value:\s*"Learned"/);
  assert.match(source, /TableName/);
  assert.match(source, /Headers/);
  assert.match(source, /SampleRows/);
  assert.match(source, /Recommendation/);
  assert.match(source, /Skip/);
});

test("智能结构路由规则页应弱化表名并默认表头匹配", () => {
  const source = readProjectFile(
    "web/src/views/config/smart-structure-routing-rules/index.vue"
  );

  assert.match(source, /辅助规则/);
  assert.match(source, /默认按表头结构和列映射识别/);
  assert.match(source, /Sheet 名\/表名（仅 Excel 兜底）/);
  assert.match(source, /matchScope:\s*"Headers"\s+as SmartStructureRoutingMatchScope/);
  assert.match(source, /form\.matchScope\s*=\s*"Headers"/);
  assert.match(source, /label:\s*"验收规格"/);
  assert.match(source, /value:\s*"AcceptanceSpec"/);
  assert.match(source, /getTableKindLabel\(row\.tableKind\)/);
});
