import assert from "node:assert/strict";
import test from "node:test";
import type { OrgUnit } from "../src/api/org-unit.ts";
import {
  countOrgUnitSubtree,
  getOrgUnitMoveTargetDisabledReason
} from "../src/views/config/org-units/move.ts";

const node = (overrides: Partial<OrgUnit>): OrgUnit => ({
  id: 1,
  parentId: null,
  unitType: 0,
  code: "ROOT",
  name: "公司",
  path: "/1/",
  depth: 0,
  sort: 0,
  isActive: true,
  children: [],
  ...overrides
});

test("组织移动仅允许选择有效的新上级节点", () => {
  const source = node({
    id: 3,
    parentId: 1,
    unitType: 2,
    code: "DEPT",
    name: "部门",
    path: "/1/3/",
    depth: 1
  });

  assert.match(
    getOrgUnitMoveTargetDisabledReason(source, node({ id: 1 }), 1) ?? "",
    /当前上级/
  );
  assert.match(
    getOrgUnitMoveTargetDisabledReason(source, source, 1) ?? "",
    /自身/
  );
  assert.match(
    getOrgUnitMoveTargetDisabledReason(
      source,
      node({ id: 4, parentId: 3, unitType: 3, path: "/1/3/4/", depth: 2 }),
      1
    ) ?? "",
    /下级/
  );
  assert.match(
    getOrgUnitMoveTargetDisabledReason(
      source,
      node({ id: 5, unitType: 1, isActive: false }),
      1
    ) ?? "",
    /停用/
  );
  assert.match(
    getOrgUnitMoveTargetDisabledReason(
      source,
      node({ id: 6, unitType: 3 }),
      1
    ) ?? "",
    /课别/
  );
  assert.match(
    getOrgUnitMoveTargetDisabledReason(
      source,
      node({ id: 7, unitType: 2 }),
      1
    ) ?? "",
    /下级类型/
  );
  assert.equal(
    getOrgUnitMoveTargetDisabledReason(
      source,
      node({ id: 8, unitType: 1, path: "/1/8/", depth: 1 }),
      1
    ),
    null
  );
});

test("移动确认展示整棵受影响子树的节点数", () => {
  const source = node({
    id: 3,
    parentId: 1,
    unitType: 2,
    path: "/1/3/",
    children: [
      node({ id: 4, parentId: 3, unitType: 3, path: "/1/3/4/" }),
      node({ id: 5, parentId: 3, unitType: 3, path: "/1/3/5/" })
    ]
  });

  assert.equal(countOrgUnitSubtree(source), 3);
});
