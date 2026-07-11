## Context

目标部署为受控内网中的单一同站 HTTP 入口，不使用 SSO、跨站 Cookie 或旧 RefreshToken JSON 客户端。HTTP 无法提供传输机密性，因此该模式必须显式启用并限制为最小拓扑；令牌暴露、CSRF、轮换和撤销防护仍需保留。

## Goals / Non-Goals

- Goals:
  - 默认 Production 继续 fail closed，仅显式配置允许内网 HTTP。
  - 内网 HTTP 使用 HttpOnly、SameSite=Strict、host-only Cookie 和精确 Origin。
  - 保留 RefreshToken 会话族、轮换、重放拒绝、CSRF、服务端登出与多标签同步。
  - 删除未实现的 SSO 和无调用方旧 JSON RefreshToken 兼容路径。
- Non-Goals:
  - 不支持公网 HTTP、跨站前后端或 SSO。
  - 不声称 HTTP 能抵御同网段窃听或中间人攻击。
  - 不替换用户、角色和权限模型。

## Decisions

### Decision 1: HTTP 降级必须显式开启

新增 `BrowserAuth:AllowInsecureHttp`，默认 `false`。Production 只有在该值为 `true` 时才接受 HTTP Origin 和非 Secure Cookie，并记录显著启动警告；不会根据请求协议自动降级。

### Decision 2: 内网 HTTP 只允许严格同站组合

内网 HTTP 模式要求 `CookieSecure=false`、`CookieSameSite=Strict`、`CookieDomain` 为空、`CookiePath=/`，Refresh Cookie 名不得使用 `__Host-` 前缀，所有允许来源必须为精确 `http://` authority 且不得包含路径、查询、片段、userinfo 或通配符。

### Decision 3: HTTPS 与 HTTP 配置互斥

默认 HTTPS 模式要求 `CookieSecure=true` 且所有 Origin 为 HTTPS。显式 HTTP 模式要求所有 Origin 为 HTTP，禁止混合 HTTP/HTTPS，避免同一会话在不同安全级别入口间漂移。

### Decision 4: RefreshToken 只走 Cookie

删除 RefreshToken JSON 请求/响应和 `X-Legacy-Auth-Client` 白名单。登录/刷新响应只返回短期 AccessToken；刷新只读取 HttpOnly Cookie。

### Decision 5: 删除 SSO 死路径

删除前端 PKCE transaction、回调和不存在的 `/sso/exchange` 调用。页面启动始终通过 Cookie refresh 恢复普通账号会话。

## Risks / Trade-offs

- HTTP 无传输加密，同网段攻击者仍可能观察或篡改流量；部署文档必须明确该剩余风险。
- 固定 IP、DNS 名或端口变化都会改变 Origin/Cookie 作用域；必须统一访问入口。
- 删除旧 JSON RefreshToken 契约是破坏性清理；本项目确认没有该类客户端。

## Migration Plan

1. 更新规范、配置守卫和单元测试。
2. 删除 SSO 与旧 JSON RefreshToken 死代码。
3. 更新 Compose/env 校验，必须显式声明内网 HTTP 模式和固定 Origin。
4. 运行 HTTP 浏览器 E2E、真实 MySQL 和 API/Web 容器 smoke。
5. 内网发布后执行旧镜像回滚演练，再单独归档本变更。

## Rollback

回滚使用旧不可变镜像且不删除数据卷。若回滚镜像仍强制 HTTPS，应同步恢复对应 HTTPS 配置；不得通过通配 CORS、关闭 CSRF 或恢复浏览器长期令牌持久化来回滚。
