# Windows Docker 部署指南

本文用于将当前项目部署到 Windows 主机的 Docker 环境中，包含：

- 前端 `web`
- 后端 `api`
- 数据库 `mysql`

本文默认部署主分支：

```text
main
```

## 1. 前置条件

目标机器需满足：

- 已安装 `Docker Desktop`
- 已启动 `Docker Desktop`
- 已安装 `Git`
- 可访问 GitHub 与 Docker 镜像仓库

先在 PowerShell 中确认：

```powershell
docker --version
docker compose version
git --version
```

## 2. 拉取代码

如果目标机器上还没有代码：

```powershell
cd D:\
git clone https://github.com/xiaocaiisxiaocai/AcceptanceSpecRecognition.git
cd .\AcceptanceSpecRecognition
git fetch origin
git switch main
git pull --ff-only origin main
git branch --show-current
```

如果目标机器上已经有代码：

```powershell
cd D:\你的项目目录\AcceptanceSpecRecognition
git fetch origin
git switch main
git pull --ff-only origin main
git branch --show-current
```

确认输出为：

```text
main
```

## 3. 启动项目

在仓库根目录执行：

```powershell
docker compose --env-file .env.docker up -d --build
```

首次构建时间会较长，属于正常现象。

## 4. 查看运行状态

```powershell
docker compose ps
docker compose logs --tail=200 mysql
docker compose logs --tail=200 api
docker compose logs --tail=200 web
```

如果需要持续查看日志：

```powershell
docker compose logs -f api
docker compose logs -f web
docker compose logs -f mysql
```

## 5. 访问地址

启动成功后可访问：

- 前端：`http://localhost`
- API 接流量就绪检查：`http://localhost:5290/health/ready`

当前支持无 SSO 的内网同站 HTTP 部署。局域网用户应始终通过一个固定的 Web 主机名或 IP 访问，由 Nginx 同站代理 API，并把该 HTTP 入口的精确来源写入 CORS/BrowserAuth。必须显式开启受控内网 HTTP 模式；不要把 `5290` API 端口直接开放给局域网用户或公网。

`.env.docker` 至少应包含以下浏览器认证组合，并将来源替换为用户实际访问的固定入口：

```env
CORS_ORIGIN_0=http://acceptance.internal
CORS_ORIGIN_1=
BROWSER_AUTH_ALLOW_INSECURE_HTTP=true
BROWSER_AUTH_REFRESH_COOKIE_NAME=acceptance-refresh
BROWSER_AUTH_COOKIE_SECURE=false
BROWSER_AUTH_COOKIE_SAME_SITE=Strict
BROWSER_AUTH_COOKIE_DOMAIN=
```

HTTP 是明文传输。HttpOnly、SameSite、CSRF 和精确 Origin 不能阻止内网监听或中间人读取登录口令、AccessToken 或 Cookie。仅可在受信任的隔离网段/VLAN中使用，并以 Windows 防火墙限制来源；不得用于互联网或不可信无线网络。无法保证链路可信时应改用内部 HTTPS。

在 PowerShell 中也可以直接验证：

```powershell
Invoke-WebRequest http://localhost:5290/health/ready
```

## 6. 默认容器说明

当前 `docker-compose.yml` 会启动以下容器：

- `acceptance-web`
- `acceptance-api`
- `acceptance-mysql`

默认端口：

- 前端：`80`
- API：`5290`
- MySQL：仅容器内访问，不映射到宿主机

## 7. 配置说明

当前部署配置由 `docker-compose.yml` 和 `.env.docker` 共同提供，不需要额外改 `appsettings.Production.json`。

非敏感默认配置如下：

- 数据库名：`acceptance_spec_db`
- 数据库用户：`acceptance`
- API 端口：`5290`
- 前端端口：`80`

从 `.env.docker.example` 创建 `.env.docker` 后，必须填写数据库 root/应用用户密码、JWT 密钥、管理员密码和普通用户密码。敏感值为空时不得部署。

启动命令必须带 `--env-file .env.docker`，否则 Docker Compose 会把未设置变量解析为空，导致 MySQL 或 API 启动失败。

前端通过 Nginx 反向代理到 API，正常情况下优先通过前端地址访问系统。

## 8. Windows 防火墙放行

如果本机能访问，但局域网其他电脑访问不到，需要放行端口：

```powershell
New-NetFirewallRule -DisplayName "Acceptance Web 80" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80
```

API 的 `5290` 端口只绑定 `127.0.0.1`，不得创建局域网入站规则。

## 9. 常用维护命令

停止容器：

```powershell
docker compose down
```

停止并删除数据卷：

```powershell
docker compose down -v
```

注意：

```text
docker compose down -v
```

会清空数据库、上传文件、DataProtection 密钥和本机数据库备份，只在确认无需保留数据时使用。生产环境应先完成可验证的离机备份，并避免执行该命令。

重新构建并启动：

```powershell
docker compose --env-file .env.docker up -d --build
```

## 10. 常见问题

### 10.1 前端打不开

先检查：

```powershell
docker compose ps
docker compose logs --tail=200 web
```

### 10.2 API 启动失败

先检查：

```powershell
docker compose logs --tail=200 api
```

### 10.3 MySQL 未就绪导致 API 连不上

先检查：

```powershell
docker compose logs --tail=200 mysql
```

等待 `mysql` 健康检查通过后，`api` 会自动继续启动。

### 10.4 端口被占用

如果 `80` 或 `5290` 被其他程序占用，可先查看端口：

```powershell
netstat -ano | findstr :80
netstat -ano | findstr :5290
```

## 11. 推荐执行顺序

建议按以下顺序执行：

1. 确认 Docker 和 Git 已安装
2. 切换到 `main` 并使用 `git pull --ff-only origin main` 更新代码
3. 执行 `docker compose --env-file .env.docker up -d --build`
4. 执行 `docker compose ps`
5. 执行 `Invoke-WebRequest http://localhost:5290/health/ready`
6. 浏览器使用配置好的固定内网 HTTP 主机名或 IP 验收，并确认所有 API 请求都经同站 Web 入口代理

## 12. 相关文件

- `docker-compose.yml`
- `src/AcceptanceSpecSystem.Api/Dockerfile`
- `web/Dockerfile`
- `deploy/nginx/default.conf`
- `docs/DEPLOY-DOCKER.md`
