## MODIFIED Requirements

### Requirement: 数据库存储的用户认证
系统 SHALL 从数据库用户表读取账号信息进行登录认证，并签发短期 JWT AccessToken；浏览器流程的长期 RefreshToken SHALL 仅通过受限 HttpOnly Cookie 传输，不得在 JSON 响应中暴露。HTTPS 模式 SHALL 使用 Secure Cookie；显式启用的同站内网 HTTP 模式 SHALL 使用非 Secure、SameSite=Strict、host-only Cookie。

#### Scenario: 浏览器登录成功
- **WHEN** 用户从允许的同站来源提供正确账号密码，且账号启用
- **THEN** 系统返回短期 AccessToken、过期时间与用户权限
- **AND** 系统通过符合当前部署模式的 HttpOnly Cookie 写入 RefreshToken
- **AND** 响应体不包含 RefreshToken

#### Scenario: 用户名密码错误或账号停用
- **WHEN** 凭据错误或账号停用
- **THEN** 系统返回 `401 Unauthorized`
- **AND** 不写入认证 Cookie

### Requirement: 刷新令牌校验
系统 SHALL 只从 HttpOnly Cookie 读取浏览器 RefreshToken，校验 CSRF/Origin、令牌、会话和用户状态，并在成功时轮换 RefreshToken、签发新的短期 AccessToken；旧 RefreshToken 在轮换后 SHALL 不可再次使用。

#### Scenario: 刷新成功
- **WHEN** RefreshToken、CSRF/Origin、会话和用户状态均有效
- **THEN** 系统返回新的短期 AccessToken
- **AND** 通过 HttpOnly Cookie 写入轮换后的 RefreshToken
- **AND** 请求或响应 JSON 均不使用 RefreshToken

#### Scenario: 刷新无效或检测到重放
- **WHEN** RefreshToken 缺失、过期、伪造、撤销或已被轮换
- **THEN** 系统拒绝刷新并清除认证 Cookie
- **AND** 检测到重放时按策略撤销会话族并记录脱敏事件

## ADDED Requirements

### Requirement: 显式受控的内网 HTTP 认证模式
系统 MUST 默认拒绝 Production HTTP 认证配置；只有显式启用内网 HTTP 模式时，才允许固定同站 HTTP Origin，并 MUST 强制非 Secure、SameSite=Strict、host-only、根 Path Cookie，禁止 `__Host-`/`__Secure-` 前缀、Domain、通配来源、混合协议和带路径或查询的来源。

#### Scenario: 合法内网 HTTP 配置
- **WHEN** 管理员显式启用内网 HTTP，配置非 Secure、Strict、无 Domain、根 Path、普通 Cookie 名和精确 HTTP authority
- **THEN** 应用允许启动并记录内网 HTTP 风险警告

#### Scenario: 未显式启用的 HTTP 配置
- **WHEN** Production 配置 HTTP Origin 或非 Secure Cookie但未启用内网 HTTP模式
- **THEN** 应用启动失败并指出不安全配置

#### Scenario: 内网 HTTP 配置越界
- **WHEN** 内网 HTTP 模式包含 Secure Cookie、非 Strict SameSite、Domain、非根 Path、Cookie 安全前缀、HTTPS/混合 Origin、通配符或非 authority 来源
- **THEN** 应用启动失败并指出冲突配置

### Requirement: 浏览器认证状态变更端点具备 CSRF 与来源防护
系统 MUST 对依赖 Cookie 的刷新和登出端点执行 CSRF 与精确允许来源校验，不允许通配来源与凭据组合。

#### Scenario: 允许来源携带有效 CSRF 证明
- **WHEN** 受信任同站前端提交有效 CSRF 证明
- **THEN** 请求可继续执行认证状态变更

#### Scenario: 来源或 CSRF 无效
- **WHEN** Origin/Referer 不匹配或 CSRF 证明缺失、错误
- **THEN** 系统拒绝请求且不得轮换或消耗 RefreshToken

### Requirement: 浏览器登出撤销服务端会话
系统 SHALL 在登出时清除认证 Cookie 并撤销服务端 RefreshToken 会话，而不是只删除客户端状态。

#### Scenario: 用户主动登出
- **WHEN** 已登录浏览器提交通过 CSRF/Origin 校验的登出请求
- **THEN** 系统撤销当前会话并清除 Cookie
- **AND** 后续刷新返回 `401 Unauthorized`
