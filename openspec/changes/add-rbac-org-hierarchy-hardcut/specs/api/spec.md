## ADDED Requirements
### Requirement: API 权限默认拒绝
系统 MUST 对所有控制器接口执行权限校验，采用 `api:resource:action` 权限码；未命中权限时返回 403。

#### Scenario: 用户缺少接口权限
- **WHEN** 已登录用户访问一个其权限集中不存在的控制器接口
- **THEN** 系统返回 403 且包含缺少的权限码信息

#### Scenario: 管理员访问接口
- **WHEN** 已登录用户持有可覆盖目标权限码的授权（如 `*:*:*` 或匹配通配权限）
- **THEN** 请求可继续进入控制器动作执行

### Requirement: 登录令牌承载组织与权限上下文
系统 MUST 在 AccessToken 中包含用户标识、公司标识与权限版本信息，以支持单公司边界和授权变更后会话管理。

#### Scenario: 登录成功下发令牌
- **WHEN** 用户使用正确用户名和密码登录
- **THEN** 返回的 AccessToken 声明包含 `user_id`、`company_id`、`permission_version`
