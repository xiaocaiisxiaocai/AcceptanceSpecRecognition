import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { isProtectedBuiltInRole } from "../src/views/config/auth-roles/roleProtection.ts";

test("common 内置角色允许管理员维护，admin 内置角色继续受保护", () => {
  assert.equal(
    isProtectedBuiltInRole({ code: "common", isBuiltIn: true }),
    false
  );
  assert.equal(
    isProtectedBuiltInRole({ code: "admin", isBuiltIn: true }),
    true
  );
  assert.equal(
    isProtectedBuiltInRole({ code: "quality", isBuiltIn: false }),
    false
  );
});

test("受保护的管理员角色权限树不可继续勾选", () => {
  const dialogSource = readFileSync(
    fileURLToPath(
      new URL(
        "../src/views/config/auth-roles/components/RoleFormDialog.vue",
        import.meta.url
      )
    ),
    "utf8"
  );

  assert.match(dialogSource, /disabled:\s*readOnly\.value/);
  assert.match(
    dialogSource,
    /if \(readOnly\.value\) \{[\s\S]*?syncCheckedKeys\(\);[\s\S]*?return;/
  );
});
