# 浏览器 E2E

测试只生成内存中的最小 DOCX，不使用仓库外业务样本。默认要求 API 已监听
`http://127.0.0.1:5291`，Playwright 会自动启动 Vite `8849` 端口。

本地准备 MySQL 和 API 后执行：

```powershell
pnpm test:e2e:install
$env:E2E_ADMIN_PASSWORD = "<测试管理员密码>"
$env:E2E_COMMON_PASSWORD = "<测试普通用户密码>"
pnpm test:e2e
```

失败产物位于 `test-results/` 和 `playwright-report/`。测试账号和数据库必须是
一次性、非生产环境资源；trace 可能包含该临时测试会话的网络元数据。

认证回归还会验证 Refresh Cookie 轮换、旧 token 重放撤销、CSRF/Origin
拒绝不消耗 token、双标签主动登出和服务端会话失效同步，以及并发 401 的
single-flight 刷新。CI 使用随机生成并掩码的临时密码，浏览器任务失败会阻断
工作流；仓库平台是否将该任务设置为分支保护 required check 仍由管理员配置。

当前产品部署边界为无 SSO 的内网同站 HTTP：浏览器只通过同一个固定主机名或
IP 访问 Web，并由 Web 入口代理 `/api`。本套 E2E 不覆盖跨站拓扑。HTTP 测试能
验证 Cookie、CSRF、Origin 和令牌轮换行为，但不能证明传输机密性；明文链路上的
监听者或中间人仍可能读取登录口令和会话数据。
