## ADDED Requirements

### Requirement: 浏览器令牌最小暴露
Web 客户端 MUST 仅在页面内存中保存短期 AccessToken，并依赖 HttpOnly RefreshToken Cookie 恢复会话；不得把 AccessToken 或 RefreshToken 写入 localStorage、sessionStorage、IndexedDB 或 JavaScript 可读 Cookie。

#### Scenario: 登录成功后保存会话
- **WHEN** 浏览器登录成功并收到 AccessToken
- **THEN** 前端只在内存 auth store 中保存 AccessToken
- **AND** 任何浏览器持久化存储中都不存在 AccessToken 或 RefreshToken

#### Scenario: 页面刷新后恢复会话
- **WHEN** 用户刷新页面导致内存 AccessToken 丢失
- **THEN** 前端通过携带 HttpOnly Cookie 和 CSRF 证明的刷新请求恢复会话
- **AND** 多个并发请求只触发一次刷新

#### Scenario: 刷新失败
- **WHEN** 会话恢复或 401 后刷新失败
- **THEN** 所有等待请求收敛为失败并只触发一次退出流程
- **AND** 前端清理内存状态并进入登录页

### Requirement: 多标签会话同步不传播令牌
Web 客户端 SHALL 在多个标签页之间同步登录、登出和权限变化事件，但不得通过 BroadcastChannel、storage event、URL 或跨窗口消息传播 AccessToken 或 RefreshToken。

#### Scenario: 一个标签页退出
- **WHEN** 用户在任一标签页完成登出
- **THEN** 其他标签页收到不含令牌的退出事件并清理内存会话

#### Scenario: 权限版本变化
- **WHEN** 任一标签页检测到权限版本失效
- **THEN** 其他标签页收到不含令牌的会话失效事件并重新认证
