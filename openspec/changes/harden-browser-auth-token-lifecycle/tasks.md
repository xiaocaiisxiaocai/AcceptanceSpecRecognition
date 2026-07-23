## 0. 范围与审批

- [x] 0.1 用户确认仅内网、同站 HTTP、不使用 SSO，且批准按该边界修改。
- [x] 0.2 保留 HttpOnly、内存 AccessToken、CSRF、Origin、轮换、重放拒绝、服务端撤销和多标签同步。

## 1. 服务端令牌与 Cookie

- [x] 1.1 增加服务端 RefreshToken 会话族、轮换、撤销和重放检测存储；安全日志不得记录原始令牌。
- [x] 1.2 登录/刷新写入 HttpOnly+SameSite Cookie，浏览器响应体停止返回 RefreshToken。
- [x] 1.3 登出清除 Cookie 并撤销服务端会话；账号停用、权限版本失效和重放检测按策略撤销会话族。
- [x] 1.4 对 Cookie 认证端点实现 Origin/Referer 与 CSRF token 校验，收紧 credentialed CORS。
- [x] 1.5 增加默认关闭的 `AllowInsecureHttp`；显式内网 HTTP 模式强制 Strict、host-only、根 Path、精确 HTTP Origin 和非前缀 Cookie 名。
- [x] 1.6 删除旧 RefreshToken JSON 请求/响应、兼容白名单、截止日期和负责人配置。

## 2. 前端

- [x] 2.1 AccessToken 仅保存在内存，页面通过 Cookie refresh 恢复会话。
- [x] 2.2 保留 401 single-flight、请求重放、单次失败退出和无令牌多标签同步。
- [x] 2.3 删除 SSO/PKCE 回调、`/sso/exchange` 死调用和对应测试/拦截器特判。

## 3. 部署、验证与收尾

- [x] 3.1 更新 Compose、生产 env 校验、CI smoke 和部署文档，内网 HTTP 必须显式启用并配置单一固定 Origin。
- [x] 3.2 增加配置组合、Cookie 属性、CSRF/Origin、轮换、重放、登出和 HTTP 浏览器 E2E。
- [x] 3.3 在真实 MySQL 上运行非 root、只读 API/Web 容器健康 smoke，并确认回滚不删除数据卷。
- [x] 3.4 运行全量后端、前端、浏览器 E2E、依赖审计、Docker build 和 OpenSpec strict 验证。
- [ ] 3.5 内网真实部署与回滚完成后，在单独 PR 中归档本变更。
