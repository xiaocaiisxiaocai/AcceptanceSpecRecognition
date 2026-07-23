# IIS 部署指南（内网）

本文适用于无正式域名的内网 IIS 部署。生产拓扑只允许一个“站点根应用”：ASP.NET Core API 挂载在 `/`，Vue 构建产物放入该发布目录的 `wwwroot`。不得再创建名为 `api` 的 IIS 子应用，否则控制器本身已有的 `/api/...` 路由会变成错误的 `/api/api/...`。

当前认证边界为无 SSO 的同站 HTTP。HTTP 不提供传输加密，只适用于受信任的隔离网段/VLAN；应通过 Windows 防火墙限制客户端来源，不得暴露到互联网或不可信无线网络。威胁边界变化时应升级为内部 HTTPS。

## 1. 唯一拓扑

- IIS 站点根应用 `/`：ASP.NET Core API
- API 业务路由：应用原生 `/api/...`，外部只出现一次 `/api`
- SPA 静态文件：根应用发布目录的 `wwwroot`
- 数据库：内网 MySQL

示例：

- 前端：`http://192.168.1.10/`
- API：`http://192.168.1.10/api/customers`
- 存活：`http://192.168.1.10/api/health/live`
- 接流量就绪：`http://192.168.1.10/api/health/ready`
- AI 能力：`http://192.168.1.10/api/health/capabilities/ai`

## 2. 前置条件

- Windows Server + IIS
- .NET 8 Hosting Bundle
- 可访问 MySQL
- 应用池账号对文件存储、Data Protection 和备份目录有明确的 Modify 权限

单根应用由 ASP.NET Core 自身完成 SPA History 回退，不依赖 URL Rewrite，也不要把 `web/public/web.config` 覆盖到 API 发布目录根部。

## 3. 构建单根发布目录

在仓库根目录执行：

```powershell
dotnet publish .\src\AcceptanceSpecSystem.Api\AcceptanceSpecSystem.Api.csproj -c Release -o .\publish\site
Push-Location .\web
pnpm install --frozen-lockfile
pnpm build
Pop-Location
New-Item -ItemType Directory -Force .\publish\site\wwwroot | Out-Null
Copy-Item .\web\dist\* .\publish\site\wwwroot -Recurse -Force
```

IIS 只创建或更新一个站点根应用：

- 物理路径：`D:\Sites\AcceptanceSpecSystem`
- 应用池：`.NET CLR = No Managed Code`，Pipeline = Integrated
- 将 `publish\site` 全量复制到该目录
- 确认站点下不存在 `api` 子应用或额外 `/api` 重写

## 4. 生产配置

使用 `appsettings.Production.json` 或等价环境变量显式配置：

- `ConnectionStrings:DefaultConnection`：真实 MySQL 连接串
- `Cors:AllowedOrigins` 与 `BrowserAuth:AllowedOrigins`：实际同站入口的精确来源，禁止 `*`
- 内网 HTTP：`BrowserAuth:AllowInsecureHttp=true`、`CookieSecure=false`、`CookieSameSite=Strict`、CookieDomain 为空
- `FileStorage:BasePath`、`DataProtection:KeysPath`：独立持久化且可写目录
- `JwtAuth:SigningKey`：至少 32 个字符的随机签名密钥
- `AuthSeed:AdminPassword` / `AuthSeed:CommonPassword`：首次部署种子口令

## 5. 数据库迁移

普通安全迁移在启动时自动执行。已有数据库若存在分类为破坏性的迁移，正常启动会拒绝执行并列出迁移标识。

完成数据库备份、独立恢复和数据校验后，在维护窗口停止所有 API 副本，使用同一发布版本执行：

```powershell
dotnet .\AcceptanceSpecSystem.Api.dll --apply-destructive-migrations --backup-verified
```

两个参数缺一不可。命令成功后退出，不启动 HTTP 服务；再启动 IIS 应用池。`--backup-verified` 是运维对已完成恢复验证的显式声明，程序不会替你创建或验证备份。旧 `--migrate-only` 仅保留为安全迁移命令兼容入口，不能批准破坏性迁移。

## 6. 验收与回滚

按顺序验证：

1. `GET /api/health/live` 返回 200。
2. `GET /api/health/ready` 返回 200；数据库、文件存储、迁移和单公司不变量均就绪。
3. `GET /api/health/capabilities/ai` 单独展示 AI 的 available/checking/degraded，不影响进程存活。
4. `POST /login`、`POST /refresh-token`、`POST /logout` 均不返回 404/405，并完成真实 Cookie 流程。
5. 打开 `/` 和任意前端 History 路径，均加载 SPA；访问不存在的 `/api/...` 不得回退为 HTML。
6. 上传并下载一个测试文件，确认持久化目录权限。

回滚应用前不得对破坏性迁移执行自动 Down。使用已演练的前向修复；确需数据库回退时停止全部副本并恢复已验证备份，然后部署与该备份结构匹配的旧版本。

## 7. 常见问题

- URL 出现 `/api/api`：删除 IIS `api` 子应用，API 必须是站点根应用。
- 前端刷新 404：确认 `index.html` 位于发布目录 `wwwroot`，并且使用的是本版本 API。
- ready 返回 503：查看服务端结构化健康日志；多公司异常只诊断数量，不自动合并或删除数据。
- API 无法启动：确认 Hosting Bundle、生产配置和应用池目录权限。
