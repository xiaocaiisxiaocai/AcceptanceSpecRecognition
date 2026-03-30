# Change: 一次性硬切 RBAC 与多层级组织授权

## Why
当前系统主要依赖 `admin` 粗粒度策略与用户表 JSON 权限，无法支撑页面级/按钮级精细授权，也无法满足跨部门协作场景下的组织层级与临时授权管理。

## What Changes
- 新增 RBAC 关系模型（角色、权限、角色权限、用户角色、数据范围）。
- 新增组织模型（公司、组织节点、用户组织关系），支持公司→事业部→部门→课别且允许跳级。
- 登录与刷新令牌改为基于关系模型聚合权限，JWT 增加 `user_id/company_id/permission_version` 声明。
- 引入 API 权限中间件，对控制器动作按 `api:resource:action` 执行默认拒绝授权。
- 前端路由增加页面权限编码，菜单按权限过滤；系统用户页面按钮接入 `v-perms`。
- 数据库迁移加入旧 `RolesJson` 到新关系表的数据迁移逻辑。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Data/*`（实体、DbContext、迁移）
  - `src/AcceptanceSpecSystem.Api/*`（鉴权中间件、登录流程、用户管理、种子初始化）
  - `web/src/router/*`、`web/src/views/config/system-users/*`（页面/按钮权限接入）
