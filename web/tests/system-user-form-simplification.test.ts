import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const source = fs.readFileSync(
  "web/src/views/config/system-users/index.vue",
  "utf8"
);

test("system user form accepts Chinese usernames", () => {
  assert.ok(
    source.includes("pattern: /^[\\p{L}\\p{N}._-]{3,64}$/u"),
    "username validation should accept Unicode letters and numbers"
  );
  assert.match(source, /placeholder="3-64个字符，支持中文、字母、数字和\._-"/);
});

test("system user management no longer exposes nickname and avatar fields", () => {
  assert.doesNotMatch(source, /<el-form-item label="昵称"/);
  assert.doesNotMatch(source, /<el-form-item label="头像"/);
  assert.doesNotMatch(source, /<el-table-column prop="nickname"/);
  assert.match(source, /nickname:\s*username/);
  assert.match(source, /avatar:\s*""/);
});
