# Docker 部署指南

本文提供本项目的 Docker 单机部署方案（`web + api + mysql`）。

## 1. 目录与文件

已提供如下文件：

- `docker-compose.yml`
- `.env.docker`（本机部署环境变量，不提交仓库）
- `src/AcceptanceSpecSystem.Api/Dockerfile`
- `web/Dockerfile`
- `deploy/nginx/default.conf`
- `.dockerignore`

## 2. 前置条件

- 已安装 Docker Desktop（或 Docker Engine + Compose v2）
- 可访问镜像仓库（拉取 `mysql`, `nginx`, `.NET`, `node` 基础镜像）

## 3. 一键启动

在仓库根目录执行：

```bash
docker compose --env-file .env.docker up -d --build
```

查看状态：

```bash
docker compose ps
docker compose logs -f api
```

## 4. 访问地址

- 前端：`http://localhost`
- API（直连排障）：`http://localhost:5290`
- API 健康检查：`http://localhost:5290/health`

当前支持无 SSO 的内网同站 HTTP 部署。局域网用户应始终通过一个固定的 Web 主机名或 IP 访问，由 Nginx 同站代理 API；API 直连端口只用于部署主机排障，不应开放给局域网用户或公网。必须显式开启受控内网 HTTP 模式，并将该 HTTP 入口的精确来源配置到 CORS 与 BrowserAuth。

HTTP 不提供传输加密。即使使用 HttpOnly、SameSite、CSRF 和精确 Origin，能够监听或篡改内网链路的人员或设备仍可能读取登录口令、AccessToken 或 Cookie。该模式只适用于受信任的隔离网段/VLAN和受防火墙约束的客户端；不得暴露到互联网或不可信无线网络。威胁边界变化时应迁移到内部 HTTPS。

说明：

- 前端通过 Nginx 反向代理到 API。
- 反向代理已包含：`/api`、`/login`、`/refresh-token`、`/logout`、`/get-async-routes`。
- Production 环境默认不提供 Swagger UI，API 启动以健康检查返回成功为准。

## 5. 默认容器与端口

- `acceptance-web`: `80 -> 8080`（容器内使用非 root Nginx 端口）
- `acceptance-api`: `5290 -> 8080`
- `acceptance-mysql`: 仅容器内访问（未映射宿主机端口）

## 6. 持久化卷

`docker-compose.yml` 中已配置：

- `mysql-data`：MySQL 数据
- `api-files`：上传文件与生成文件（`FileStorage`）
- `api-dpkeys`：DataProtection key ring
- `api-backups`：数据库备份文件（容器内 `/app/backups`）

`api-backups` 只保证容器重建时本机备份仍在，不等于离机备份。生产环境应定期把备份复制到异机或对象存储，并执行恢复演练。

## 7. 关键环境变量

从 `.env.docker.example` 创建本地 `.env.docker` 后，必须填写以下敏感配置：

- `MYSQL_ROOT_PASSWORD`
- `MYSQL_PASSWORD`
- `JWT_SIGNING_KEY`（至少 32 个字符的随机值）
- `AUTH_SEED_ADMIN_PASSWORD`
- `AUTH_SEED_COMMON_PASSWORD`

非敏感默认配置为 `MYSQL_DATABASE=acceptance_spec_db`、`MYSQL_USER=acceptance`。敏感值为空时不得部署。

其他常用配置：

- `CORS_ORIGIN_0`
- `CORS_ORIGIN_1`
- `BROWSER_AUTH_ALLOW_INSECURE_HTTP=true`
- `BROWSER_AUTH_COOKIE_SECURE=false`
- `BROWSER_AUTH_REFRESH_COOKIE_NAME=acceptance-refresh`
- `BROWSER_AUTH_COOKIE_SAME_SITE=Strict`

说明：

- 启动命令必须带 `--env-file .env.docker`，否则 Docker Compose 会把未设置变量解析为空。
- 通过固定的 HTTP 主机名或 IP 走 Nginx 同源访问，`CORS_ORIGIN_*` 必须与该入口精确一致。
- 不支持 SSO 或前后端跨站部署；不要使用通配符 `*`，也不要配置 Cookie Domain。

## 8. 停止与清理

停止容器：

```bash
docker compose down
```

停止并删除卷（会清空数据库、上传文件、DataProtection 密钥和本机数据库备份）：

```bash
docker compose down -v
```

生产环境不要执行带 `-v` 的命令，除非已经确认所有数据均可丢弃或已完成可验证的离机备份。

## 9. 常见问题

1. 前端登录报 `ECONNREFUSED`  
   先看 `docker compose logs -f api`，确认 API 已启动并迁移成功。

2. API 启动失败（JWT 密钥长度）  
   检查是否使用了 `docker compose --env-file .env.docker up -d --build`，且 `JWT_SIGNING_KEY` 至少 32 字符。

3. MySQL 启动后 API 仍连接失败  
   等待 `mysql` 健康检查通过，或查看 `docker compose logs -f mysql`。
