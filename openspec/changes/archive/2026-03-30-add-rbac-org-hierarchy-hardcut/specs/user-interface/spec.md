## ADDED Requirements
### Requirement: 页面级权限过滤
前端 MUST 支持基于页面权限码过滤菜单与路由可见性，权限码采用 `page:module:page` 形式。

#### Scenario: 用户缺少页面权限
- **WHEN** 登录用户权限集中不包含目标页面权限码
- **THEN** 菜单中不展示该页面入口

### Requirement: 按钮级权限控制
前端 MUST 支持基于 `v-perms` 的按钮级权限控制，权限码采用 `btn:resource:action` 形式。

#### Scenario: 用户缺少按钮权限
- **WHEN** 登录用户不具备目标按钮权限码
- **THEN** 对应操作按钮不显示或不可操作
