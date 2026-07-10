import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const expectedCancelChecks = new Map<string, number>([
  ["web/src/views/base-data/customers/index.vue", 2],
  ["web/src/views/base-data/processes/index.vue", 2],
  ["web/src/views/base-data/machine-models/index.vue", 2],
  ["web/src/views/base-data/specs/components/SpecTable.vue", 2],
  ["web/src/views/config/system-users/index.vue", 1],
  ["web/src/views/config/smart-structure-routing-rules/index.vue", 1],
  ["web/src/views/config/prompt-templates/index.vue", 1],
  ["web/src/views/config/auth-roles/index.vue", 1],
  ["web/src/views/config/column-mapping-rules/index.vue", 2],
  ["web/src/views/config/ai-services/index.vue", 2]
]);

for (const [relativePath, minimumChecks] of expectedCancelChecks) {
  test(`${relativePath} 的破坏性操作应区分主动取消与请求错误`, () => {
    const source = readFileSync(resolve(process.cwd(), relativePath), "utf8");

    assert.match(
      source,
      /import \{ isMessageBoxCancel \} from "@\/utils\/message-box";/
    );
    assert.match(source, /getRequestErrorMessage\(error,/);
    assert.ok(
      (source.match(/isMessageBoxCancel\(error\)/g) ?? []).length >=
        minimumChecks,
      `expected at least ${minimumChecks} guarded catches in ${relativePath}`
    );
    assert.doesNotMatch(source, /catch\s*\{\s*\/\/\s*(?:用户取消|cancelled)/i);
  });
}
