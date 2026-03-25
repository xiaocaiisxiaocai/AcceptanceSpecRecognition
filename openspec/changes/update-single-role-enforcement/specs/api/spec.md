## ADDED Requirements
### Requirement: 系统用户与认证接口采用单角色模型
系统 MUST 在系统用户管理、登录和刷新令牌接口中使用单角色字段 `roleCode`，不再暴露角色数组。

#### Scenario: 创建用户提交单角色
- **WHEN** 管理员调用系统用户创建接口并提交 `roleCode`
- **THEN** 系统为该用户建立唯一角色关系，并返回单个 `roleCode`

#### Scenario: 更新用户提交多个角色
- **WHEN** 客户端仍以旧格式提交角色数组或等价多角色数据
- **THEN** 系统拒绝请求并返回参数错误

#### Scenario: 登录成功返回单角色
- **WHEN** 用户登录成功或刷新令牌成功
- **THEN** 返回数据包含单个 `roleCode`，且不包含 `roles` 数组

### Requirement: 单角色模型下保留管理员边界保护
系统 MUST 在单角色模型下继续阻止最后一个启用中的 `admin` 用户被降级、停用或删除。

#### Scenario: 尝试移除最后一个启用中的 admin
- **WHEN** 管理员更新、停用或删除系统中最后一个启用且角色为 `admin` 的用户
- **THEN** 系统拒绝请求并返回明确错误
