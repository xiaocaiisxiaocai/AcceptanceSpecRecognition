import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const source = readFileSync(
  resolve(process.cwd(), "web/src/views/smart-fill/components/MatchConfig.vue"),
  "utf8"
);

test("智能填充匹配配置组件应在 mounted 生命周期内加载基础数据", () => {
  assert.match(
    source,
    /import \{ computed, onBeforeUnmount, onMounted, ref, watch \} from "vue";/
  );
  assert.match(
    source,
    /onMounted\(\(\) => \{\s*loadCustomers\(\);\s*loadProcesses\(\);\s*loadMachineModels\(\);\s*loadAiServices\(\);\s*\}\);/s
  );
});

test("智能填充匹配配置组件卸载时应取消主数据选项请求", () => {
  assert.match(
    source,
    /onBeforeUnmount\(\(\) => \{\s*customerOptionsController\?\.abort\(\);\s*processOptionsController\?\.abort\(\);\s*machineModelOptionsController\?\.abort\(\);\s*\}\);/s
  );
});

test("智能填充匹配配置组件不应在模块顶层直接触发加载请求", () => {
  assert.doesNotMatch(
    source,
    /\nloadCustomers\(\);\s*\nloadProcesses\(\);\s*\nloadMachineModels\(\);\s*\nloadAiServices\(\);/
  );
});
