import assert from "node:assert/strict";
import test from "node:test";
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
