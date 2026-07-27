import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const source = fs.readFileSync(
  "web/src/views/config/system-users/index.vue",
  "utf8"
);

test("system user create and reset forms enforce the 4-to-200 password policy", () => {
  assert.match(
    source,
    /min:\s*4,\s*max:\s*200,\s*message:\s*"密码长度必须在4到200位之间"/
  );
  assert.match(
    source,
    /min:\s*4,\s*max:\s*200,\s*message:\s*"新密码长度必须在4到200位之间"/
  );
  assert.match(source, /placeholder="4到200位"/);
  assert.doesNotMatch(source, /min:\s*12|至少12位/);
});
