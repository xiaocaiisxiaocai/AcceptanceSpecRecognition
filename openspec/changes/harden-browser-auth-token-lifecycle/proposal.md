# Change: 加固内网浏览器认证令牌生命周期

## Why

系统部署在不对公网开放的受控内网，Web 与 API 采用同站 HTTP，不使用 SSO，也没有需要 RefreshToken JSON 契约的旧客户端。现有 HttpOnly Cookie、内存 AccessToken、令牌轮换、CSRF 和会话撤销仍是必要防护，但 Production 强制 HTTPS、`Secure`/`__Host-` Cookie 以及未落地的 SSO 代码与实际拓扑不符。

## What Changes

- 保留 HttpOnly RefreshToken Cookie、仅内存 AccessToken、轮换/重放拒绝、服务端撤销、CSRF/Origin 和多标签会话同步。
- 增加必须显式开启的受控内网 HTTP 模式；默认 Production 仍要求 HTTPS。
- 内网 HTTP 模式强制同站 `SameSite=Strict`、host-only、`Path=/`、精确 HTTP Origin，并禁止 `Secure`、`__Host-` 前缀、Cookie Domain、通配 CORS 和跨站配置。
- 删除未接通后端的 SSO/PKCE 前端死代码和规范范围。
- 删除默认关闭且无调用方的旧 JSON RefreshToken 兼容契约，浏览器 RefreshToken 只通过 HttpOnly Cookie 传输。
- 用真实 HTTP 浏览器 E2E 和只读非 root Docker smoke 验证登录、恢复、轮换、CSRF、重放、登出和回滚。

## Impact

- Affected specs: `api`, `user-interface`
- Affected code: BrowserAuth 配置与启动校验、认证控制器、前端启动/HTTP 拦截器、Docker/部署配置和认证测试
- Breaking risk: 旧 JSON RefreshToken 客户端和 SSO 半成品入口被移除；内网 HTTP 必须通过显式配置启用
