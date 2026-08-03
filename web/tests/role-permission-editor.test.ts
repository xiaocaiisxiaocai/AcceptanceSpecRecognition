import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";
import {
  buildPermissionEditorView,
  normalizePermissionCodes,
  replacePermissionGroupSelection
} from "../src/views/config/auth-roles/permissionEditor.ts";

const permissions = [
  {
    id: 1,
    code: "btn:system-user:create",
    name: "按钮-system-user-create",
    permissionType: 1,
    resource: "system-user",
    action: "create"
  },
  {
    id: 2,
    code: "btn:system-user:reset-password",
    name: "按钮-system-user-reset-password",
    permissionType: 1,
    resource: "system-user",
    action: "reset-password"
  },
  {
    id: 3,
    code: "btn:org-unit:move",
    name: "按钮-org-unit-move",
    permissionType: 1,
    resource: "org-unit",
    action: "move"
  },
  {
    id: 4,
    code: "api:system-user:read",
    name: "接口-system-user-read",
    permissionType: 2,
    resource: "system-user",
    action: "read"
  },
  {
    id: 5,
    code: "page:config:system-users",
    name: "页面-系统用户",
    permissionType: 0,
    resource: "system-user",
    action: "read"
  }
];

test("按权限类型与业务资源分组，并返回各层已选计数", () => {
  const view = buildPermissionEditorView({
    permissions,
    selectedCodes: [
      "btn:system-user:create",
      "btn:org-unit:move",
      "api:system-user:read"
    ],
    activeType: 1,
    keyword: "",
    selectedOnly: false
  });

  assert.deepEqual(
    view.types.map(item => [item.value, item.selectedCount, item.totalCount]),
    [
      [3, 0, 0],
      [0, 0, 1],
      [1, 2, 3],
      [2, 1, 1]
    ]
  );
  assert.deepEqual(
    view.groups.map(group => [
      group.resource,
      group.label,
      group.selectedCount,
      group.totalCount
    ]),
    [
      ["system-user", "系统用户", 1, 2],
      ["org-unit", "组织管理", 1, 1]
    ]
  );
});

test("搜索覆盖业务名称、编码、资源和动作，并让业务文案优先", () => {
  const byBusinessName = buildPermissionEditorView({
    permissions,
    selectedCodes: [],
    activeType: 1,
    keyword: "重置密码",
    selectedOnly: false
  });
  assert.equal(byBusinessName.groups.length, 1);
  assert.equal(byBusinessName.groups[0].items.length, 1);
  assert.equal(byBusinessName.groups[0].items[0].primaryLabel, "重置密码");
  assert.equal(
    byBusinessName.groups[0].items[0].secondaryLabel,
    "btn:system-user:reset-password"
  );

  for (const keyword of ["btn:org-unit:move", "org-unit", "move"]) {
    const result = buildPermissionEditorView({
      permissions,
      selectedCodes: [],
      activeType: 1,
      keyword,
      selectedOnly: false
    });
    assert.equal(result.groups.length, 1);
    assert.equal(result.groups[0].resource, "org-unit");
  }
});

test("仅看已选只过滤可见项，不改变被隐藏的 permissionCodes", () => {
  const selectedCodes = [
    "btn:system-user:create",
    "btn:org-unit:move",
    "api:system-user:read"
  ];
  const view = buildPermissionEditorView({
    permissions,
    selectedCodes,
    activeType: 1,
    keyword: "系统用户",
    selectedOnly: true
  });

  assert.deepEqual(
    view.groups.flatMap(group => group.items.map(item => item.code)),
    ["btn:system-user:create"]
  );
  assert.deepEqual(selectedCodes, [
    "btn:system-user:create",
    "btn:org-unit:move",
    "api:system-user:read"
  ]);
});

test("分组全选和清空只修改目标资源，并始终输出去重 permissionCodes", () => {
  const systemUserCodes = [
    "btn:system-user:create",
    "btn:system-user:reset-password"
  ];
  const selected = replacePermissionGroupSelection(
    ["btn:org-unit:move", "btn:system-user:create"],
    systemUserCodes,
    true
  );
  assert.deepEqual(selected, [
    "btn:org-unit:move",
    "btn:system-user:create",
    "btn:system-user:reset-password"
  ]);

  assert.deepEqual(
    replacePermissionGroupSelection(selected, systemUserCodes, false),
    ["btn:org-unit:move"]
  );
  assert.deepEqual(normalizePermissionCodes(["a", "a", " ", "b"]), ["a", "b"]);
});

test("角色弹窗使用类型切换、资源分组、搜索和仅看已选控件", () => {
  const source = readFileSync(
    fileURLToPath(
      new URL(
        "../src/views/config/auth-roles/components/RoleFormDialog.vue",
        import.meta.url
      )
    ),
    "utf8"
  );

  assert.match(source, /data-testid="permission-editor"/);
  assert.match(source, /data-testid="permission-search"/);
  assert.match(source, /data-testid="permission-selected-only"/);
  assert.match(source, /permission-resource-group/);
  assert.doesNotMatch(source, /<el-tree/);
});

test("角色弹窗使用紧凑密度并为范围配置保留可见空间", () => {
  const source = readFileSync(
    fileURLToPath(
      new URL(
        "../src/views/config/auth-roles/components/RoleFormDialog.vue",
        import.meta.url
      )
    ),
    "utf8"
  );

  assert.match(source, /:rows="2"/);
  assert.match(
    source,
    /\.permission-resource-list\s*\{[\s\S]*?max-height:\s*300px/
  );
  assert.match(source, /\.permission-option\s*\{[\s\S]*?min-height:\s*40px/);
  assert.match(
    source,
    /\.role-form-dialog \.el-form-item\)[\s\S]*?margin-bottom:\s*12px/
  );
});
