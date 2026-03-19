## ADDED Requirements

### Requirement: 数据库存储的用户认证
系统 SHALL 从数据库用户表读取账号信息进行登录认证，并签发 JWT 令牌。

#### Scenario: 登录成功返回令牌
- **WHEN** 用户提供正确的用户名和密码，且账号处于启用状态
- **THEN** 系统返回 `accessToken`、`refreshToken` 与用户角色权限信息

#### Scenario: 用户名或密码错误
- **WHEN** 用户名不存在或密码校验失败
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 账号被停用
- **WHEN** 用户账号存在但 `IsActive = false`
- **THEN** 系统返回 `401 Unauthorized`

---

### Requirement: 刷新令牌校验
系统 SHALL 校验刷新令牌有效性，并在用户仍有效时重新签发令牌。

#### Scenario: 刷新令牌无效
- **WHEN** 客户端提交无效、过期或伪造的 `refreshToken`
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 刷新时用户不存在或已停用
- **WHEN** 刷新令牌有效但关联用户不存在或被停用
- **THEN** 系统返回 `401 Unauthorized`

#### Scenario: 刷新成功
- **WHEN** 刷新令牌有效且用户处于启用状态
- **THEN** 系统返回新的 `accessToken` 与 `refreshToken`

---

### Requirement: 管理接口角色授权
系统 MUST 对管理类接口执行 `admin` 角色授权。

#### Scenario: 普通角色访问管理接口
- **WHEN** `common` 角色用户访问管理接口
- **THEN** 系统返回 `403 Forbidden`

#### Scenario: 未登录访问管理接口
- **WHEN** 未携带有效登录身份访问管理接口
- **THEN** 系统返回 `401 Unauthorized`

---

### Requirement: 系统用户管理API
系统 SHALL 提供受 `admin` 角色保护的系统用户管理接口。

#### Scenario: 查询系统用户列表
- **WHEN** 管理员请求系统用户列表接口
- **THEN** 系统返回分页用户数据，包含账号启用状态与角色权限信息

#### Scenario: 创建系统用户
- **WHEN** 管理员提交合法的新用户信息（用户名、密码、角色）
- **THEN** 系统创建用户并返回用户详情

#### Scenario: 更新系统用户
- **WHEN** 管理员更新用户昵称、角色、权限或启用状态
- **THEN** 系统保存变更并返回更新后的用户信息

#### Scenario: 重置用户密码
- **WHEN** 管理员提交新密码
- **THEN** 系统更新用户密码哈希并使新密码可用于后续登录

#### Scenario: 禁止移除最后一个启用admin
- **WHEN** 管理员尝试删除或停用最后一个启用状态的 `admin` 用户
- **THEN** 系统拒绝请求并返回业务错误
