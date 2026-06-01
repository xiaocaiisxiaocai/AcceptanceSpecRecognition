# Windows Docker 部署指南

本文用于将当前项目部署到 Windows 主机的 Docker 环境中，包含：

- 前端 `web`
- 后端 `api`
- 数据库 `mysql`

本文默认基于当前功能分支：

```text
feat/add-ai-equivalence-adjudication
```

不是 `main` 分支。

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
git checkout feat/add-ai-equivalence-adjudication
git pull origin feat/add-ai-equivalence-adjudication
git branch --show-current
```

如果目标机器上已经有代码：

```powershell
cd D:\你的项目目录\AcceptanceSpecRecognition
git fetch origin
git checkout feat/add-ai-equivalence-adjudication
git pull origin feat/add-ai-equivalence-adjudication
git branch --show-current
```

确认输出为：

```text
feat/add-ai-equivalence-adjudication
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
- 前端局域网访问：`http://192.168.132.68`
- API 健康检查：`http://localhost:5290/health`
- API 局域网访问：`http://192.168.132.68:5290/health`

在 PowerShell 中也可以直接验证：

```powershell
Invoke-WebRequest http://localhost:5290/health
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

## 7. 默认配置说明

当前部署配置由 `docker-compose.yml` 和 `.env.docker` 共同提供，不需要额外改 `appsettings.Production.json`。

关键点如下：

- 数据库名：`acceptance_spec_ai_equivalence_adjudication_db`
- 数据库用户：`acceptance`
- 数据库密码：`acceptance123`
- JWT 密钥：`AcceptanceSpec_DockerJwtKey_2026_ReplaceWithLongRandom`
- 默认管理员密码：`Admin@20260403`
- 默认普通用户密码：`Common@20260403`
- API 端口：`5290`
- 前端端口：`80`

启动命令必须带 `--env-file .env.docker`，否则 Docker Compose 会把未设置变量解析为空，导致 MySQL 或 API 启动失败。

前端通过 Nginx 反向代理到 API，正常情况下优先通过前端地址访问系统。

## 8. Windows 防火墙放行

如果本机能访问，但局域网其他电脑访问不到，需要放行端口：

```powershell
New-NetFirewallRule -DisplayName "Acceptance Web 80" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 80
New-NetFirewallRule -DisplayName "Acceptance API 5290" -Direction Inbound -Action Allow -Protocol TCP -LocalPort 5290
```

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

会清空数据库和上传文件，只在确认无需保留数据时使用。

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
2. 切换到 `feat/add-ai-equivalence-adjudication`
3. 执行 `docker compose --env-file .env.docker up -d --build`
4. 执行 `docker compose ps`
5. 执行 `Invoke-WebRequest http://localhost:5290/health`
6. 浏览器打开 `http://192.168.132.68`

## 12. 相关文件

- `docker-compose.yml`
- `src/AcceptanceSpecSystem.Api/Dockerfile`
- `web/Dockerfile`
- `deploy/nginx/default.conf`
- `docs/DEPLOY-DOCKER.md`
