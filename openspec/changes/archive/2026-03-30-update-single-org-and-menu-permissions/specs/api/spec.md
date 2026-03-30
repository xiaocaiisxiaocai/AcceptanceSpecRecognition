## ADDED Requirements
### Requirement: 系统用户与权限接口采用单组织和菜单权限模型
系统 MUST 在系统用户管理和权限相关接口中使用单组织字段，并暴露菜单权限能力。

#### Scenario: 创建用户提交单组织
- **WHEN** 管理员调用系统用户创建接口并提交 `orgUnitId`
- **THEN** 系统为该用户建立唯一组织关系，并返回单个 `orgUnitId` / `orgUnitName`

#### Scenario: 更新用户提交旧组织数组
- **WHEN** 客户端仍以旧格式提交 `orgUnitIds`、`orgUnits` 或 `primaryOrgUnitId`
- **THEN** 系统拒绝请求并返回参数错误

#### Scenario: 路由权限包含菜单权限
- **WHEN** 用户登录后拉取路由或权限数据
- **THEN** 返回结果能够区分菜单权限与页面权限，并据此控制导航容器和页面节点
