import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/components/MatchConfig.vue"),
  "utf8"
);

test("智能填充匹配配置组件应在 mounted 生命周期内加载运行时 AI 选择", () => {
  assert.match(source, /onActivated,[\s\S]*onDeactivated,[\s\S]*onMounted/);
  assert.match(
    source,
    /onMounted\(\(\) => \{\s*void loadAiServices\(\);\s*\}\);/s
  );
});

test("智能填充匹配配置组件应在缓存页面重新激活时刷新运行时 AI 选择", () => {
  assert.match(
    source,
    /onActivated\(\(\) => \{\s*void loadAiServices\(\);\s*\}\);/
  );
  assert.match(source, /createAiSelectionRetryController/);
  assert.match(
    source,
    /const stopAiSelectionRequests = \(\) => \{[\s\S]*aiSelectionController\?\.abort\(\);[\s\S]*aiSelectionRetry\.cancel\(\);[\s\S]*onDeactivated\(stopAiSelectionRequests\);/
  );
});

test("智能填充匹配配置组件卸载时应取消 AI 请求和延迟滚动", () => {
  assert.match(
    source,
    /onBeforeUnmount\(\(\) => \{\s*stopAiSelectionRequests\(\);\s*cancelExpandedSectionReveal\(\);\s*\}\);/s
  );
});

test("智能填充业务动作应等待 checking 状态收敛后再返回", () => {
  assert.match(source, /waitForRuntimeAiSelection/);
  assert.match(
    source,
    /const refreshAiServicesForAction = \(\) => \{\s*stopAiSelectionRequests\(\);\s*return loadAiServices\(true, true\);\s*\};/s
  );
  assert.match(source, /refreshAiServices:\s*refreshAiServicesForAction/);
});

test("智能填充匹配配置不应重复加载或编辑第一步已确认的范围", () => {
  assert.doesNotMatch(
    source,
    /getCustomerList|getProcessList|getMachineModelList/
  );
  assert.doesNotMatch(source, /scope\?: SmartFillScope|scopeChange/);
  assert.doesNotMatch(source, />匹配范围</);
});
