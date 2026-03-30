# Change: 强制系统用户采用单组织并增加菜单权限

## Why
当前系统用户组织模型仍按“多组织 + 主组织”设计，和业务规则“一个用户只会属于一个组织”不一致；同时现有权限只覆盖页面、按钮和 API，左侧导航容器菜单无法被角色独立控制，导致菜单显隐和页面访问边界不够清晰。

## What Changes
- 将系统用户创建、编辑、查询接口统一改为单组织字段 `orgUnitId`，不再暴露 `orgUnits[]`、`orgUnitIds[]`、`primaryOrgUnitId`。
- 保留 `AuthUserOrgUnits` 关系表，但通过迁移清洗历史多组织数据，并为 `UserId` 增加唯一约束。
- 历史多组织数据按“`IsPrimary` 优先，否则保留最早一条组织关系”迁移；无组织用户补到公司根组织。
- 权限类型从页面、按钮、API 扩展为菜单、页面、按钮、API，并为顶层导航容器补齐菜单权限码。
- 路由过滤与权限字典同步支持菜单权限，系统用户页改为单组织选择和展示。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Api/*`（用户管理、认证访问上下文、权限种子、路由权限接口）
  - `src/AcceptanceSpecSystem.Data/*`（用户组织迁移、唯一约束、仓储查询）
  - `web/src/api/*`、`web/src/store/*`、`web/src/router/*`、`web/src/views/config/system-users/*`、`web/src/views/rbac/permissions/*`
