import assert from "node:assert/strict";
import test from "node:test";
import {
  getAllowedChildTypes,
  type OrgUnitType
} from "../src/views/config/org-units/hierarchy.ts";
import {
  normalizeScopeOrgUnitIds,
  validateScopeOrgUnitIds
} from "../src/views/config/auth-roles/roleScope.ts";

test("组织层级只允许向下创建并支持跳级", () => {
  assert.deepEqual(getAllowedChildTypes(0), [1, 2, 3]);
  assert.deepEqual(getAllowedChildTypes(1), [2, 3]);
  assert.deepEqual(getAllowedChildTypes(2), [3]);
  assert.deepEqual(getAllowedChildTypes(3), []);
  assert.deepEqual(getAllowedChildTypes(99 as OrgUnitType), []);
});

test("单节点范围只保留一个组织，自定义范围保留去重后的多个组织", () => {
  assert.deepEqual(normalizeScopeOrgUnitIds(1, [8, 9, 8]), [8]);
  assert.deepEqual(normalizeScopeOrgUnitIds(2, [8, 9]), [8]);
  assert.deepEqual(normalizeScopeOrgUnitIds(3, [8, 9, 8, 0, -1]), [8, 9]);
  assert.deepEqual(normalizeScopeOrgUnitIds(0, [8]), []);
  assert.deepEqual(normalizeScopeOrgUnitIds(4, [8]), []);
});

test("节点、子树和自定义范围必须选择符合数量要求的组织", () => {
  assert.equal(validateScopeOrgUnitIds(1, []), "请选择一个组织节点");
  assert.equal(validateScopeOrgUnitIds(2, [8, 9]), "请选择一个组织节点");
  assert.equal(validateScopeOrgUnitIds(3, []), "请至少选择一个组织节点");
  assert.equal(validateScopeOrgUnitIds(3, [8, 9]), null);
  assert.equal(validateScopeOrgUnitIds(4, []), null);
});
