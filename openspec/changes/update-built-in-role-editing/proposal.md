# Change: 放开内置角色编辑能力

## Why
当前系统把内置角色 `admin/common` 的编辑能力整体锁死，导致管理员无法直接调整既有内置角色的权限配置与数据范围，不符合当前管理需求。

## What Changes
- 允许内置角色进入编辑流程并保存修改
- 保留内置角色删除限制
- 前端角色管理页面同步放开内置角色编辑入口
- 补充内置角色可编辑、但不可删除的测试覆盖

## Impact
- Affected specs: `user-interface`, `api`
- Affected code: `src/AcceptanceSpecSystem.Api/Controllers/AuthRolesController.cs`, `web/src/views/config/auth-roles/index.vue`, `tests/AcceptanceSpecSystem.Api.Tests/AuthRolesTests.cs`
