import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/components/MatchConfig.vue"),
  "utf8"
);

test("智能填充匹配配置组件应在 mounted 生命周期内加载基础数据", () => {
  assert.match(source, /onActivated,[\s\S]*onDeactivated,[\s\S]*onMounted/);
  assert.match(
    source,
    /onMounted\(\(\) => \{\s*loadCustomers\(\);\s*loadProcesses\(\);\s*loadMachineModels\(\);\s*loadAiServices\(\);\s*\}\);/s
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

test("智能填充匹配配置组件卸载时应取消主数据选项请求", () => {
  assert.match(
    source,
    /onBeforeUnmount\(\(\) => \{\s*customerOptionsController\?\.abort\(\);\s*processOptionsController\?\.abort\(\);\s*machineModelOptionsController\?\.abort\(\);\s*stopAiSelectionRequests\(\);\s*\}\);/s
  );
});

test("智能填充匹配配置组件不应在模块顶层直接触发加载请求", () => {
  assert.doesNotMatch(
    source,
    /\nloadCustomers\(\);\s*\nloadProcesses\(\);\s*\nloadMachineModels\(\);\s*\nloadAiServices\(\);/
  );
});

test("智能填充匹配配置应由父页面同步客户、制程和机型范围", () => {
  assert.match(source, /scope\?: SmartFillScope/);
  assert.match(source, /props\.scope\?\.customerId/);
  assert.match(source, /props\.scope\?\.processId/);
  assert.match(source, /props\.scope\?\.machineModelId/);
  assert.match(source, /syncingScopeFromParent/);
});
