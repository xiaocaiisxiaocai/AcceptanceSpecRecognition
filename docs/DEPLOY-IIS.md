# IIS 部署指南（内网）

本文适用于内网 IIS 部署场景（无正式域名），默认前后端通过同一台 IIS 提供服务。

## 1. 推荐拓扑

建议使用同站点路径方案，避免跨站 CORS 问题：

- 站点根路径：`/`（前端静态文件）
- API 子应用：`/api`（ASP.NET Core）
- 数据库：内网 MySQL

示例访问：

- 前端：`http://192.168.1.10/`
- API：`http://192.168.1.10/api`
- Swagger：`http://192.168.1.10/api/swagger`
- 健康检查：`http://192.168.1.10/api/health`

## 2. 前置条件

- Windows Server + IIS
- 已安装 `.NET 8 Hosting Bundle`
- 已安装 URL Rewrite（用于前端 SPA 路由回退）
- 可访问 MySQL 实例
- 部署账号对站点目录有读写权限

## 3. 后端发布（API）

在仓库根目录执行：

```powershell
dotnet publish .\src\AcceptanceSpecSystem.Api\AcceptanceSpecSystem.Api.csproj -c Release -o .\publish\api
```

IIS 中创建（或更新）API 应用：

- 物理路径：`D:\Sites\AcceptanceSpecSystem\api`
- 应用池：`.NET CLR = No Managed Code`，`Pipeline = Integrated`
- 将 `publish\api` 内容复制到该目录

## 4. 前端发布（Web）

```powershell
cd .\web
pnpm install
pnpm build
```

IIS 站点根目录示例：

- 物理路径：`D:\Sites\AcceptanceSpecSystem\web`
- 将 `web\dist` 全量复制到该目录

说明：

- 项目已提供 `web/public/web.config`，构建后会进入 `dist`，用于 Vue History 路由回退。
- 该规则已排除 `/api` 路径，避免影响 API 子应用。

## 5. API 子应用挂载

在 IIS 站点下新增应用：

- 别名：`api`
- 物理路径：`D:\Sites\AcceptanceSpecSystem\api`
- 应用池：使用 API 专属应用池

完成后前端调用 `/api/*` 将由同站点直接路由到后端应用，无需额外反向代理。

## 6. 生产配置（已提供可运行默认值）

使用 `src/AcceptanceSpecSystem.Api/appsettings.Production.json`：

- `ConnectionStrings:DefaultConnection`：默认已指向 `127.0.0.1:3306`，如数据库不在本机请改为实际连接串
- `Cors:AllowedOrigins`：默认 `["*"]`（内网无固定域名可直接使用）
- `FileStorage:BasePath`：默认 `D:\AcceptanceSpecData`（建议保留为独立持久化目录）
- `JwtAuth:SigningKey`：已配置可用值，建议上线后替换为你自己的长随机密钥
- 系统账号存储在数据库 `SystemUsers` 表，不再写在 `appsettings`

首次启动（且 `SystemUsers` 为空）会自动写入默认账号：

- `admin / Admin@123456`
- `common / Common@123456`

建议首次登录后立即在数据库中更新 `PasswordHash`（PBKDF2）并禁用不需要的账号。

示例：

- `D:\AcceptanceSpecData`

目录下会自动创建：

- `uploads\word-files\yyyy-MM-dd\...`
- `uploads\excel-files\yyyy-MM-dd\...`
- `uploads\filled-files\yyyy-MM-dd\...`

权限要求：

- 给 API 应用池账号授予 `FileStorage:BasePath` 的 `Modify` 权限

## 7. 数据库迁移

系统启动时会自动执行迁移（`Testing` 环境除外）。  
如果你希望发布前手工迁移，可在发布机执行：

```powershell
dotnet ef database update -p .\src\AcceptanceSpecSystem.Data -s .\src\AcceptanceSpecSystem.Api
```

## 8. 验证清单

1. 打开 `http://<IIS地址>/`，前端可正常加载
2. 打开 `http://<IIS地址>/api/health`，返回 `healthy`
3. 上传 Word/Excel 后，`FileStorage:BasePath` 下有实际文件
4. 执行智能填充，Word 下载与 Excel 写回正常
5. 打开 `http://<IIS地址>/api/swagger` 可查看接口文档

## 9. 常见问题

- 前端刷新 404：确认 `dist` 下有 `web.config`，且 URL Rewrite 已安装
- 上传失败/无权限：检查 `FileStorage:BasePath` 目录权限
- 跨域报错：若保留 `["*"]` 通常不会触发；如你改成白名单，需与实际访问地址完全一致（协议/端口都要匹配）
- API 无法启动：确认已安装 `.NET 8 Hosting Bundle`，并查看 IIS 应用事件日志
