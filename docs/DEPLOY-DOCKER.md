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
- Swagger：`http://localhost/swagger`

说明：

- 前端通过 Nginx 反向代理到 API。
- 反向代理已包含：`/api`、`/login`、`/refresh-token`、`/get-async-routes`。

## 5. 默认容器与端口

- `acceptance-web`: `80 -> 80`
- `acceptance-api`: `5290 -> 8080`
- `acceptance-mysql`: 仅容器内访问（未映射宿主机端口）

## 6. 持久化卷

`docker-compose.yml` 中已配置：

- `mysql-data`：MySQL 数据
- `api-files`：上传文件与生成文件（`FileStorage`）
- `api-dpkeys`：DataProtection key ring

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

说明：

- 启动命令必须带 `--env-file .env.docker`，否则 Docker Compose 会把未设置变量解析为空。
- 默认建议通过 `http://localhost` 走 Nginx 同源访问。
- 如果需要前后端分站访问，再把 `CORS_ORIGIN_*` 改成你的实际来源地址；不要使用通配符 `*`。

## 8. 停止与清理

停止容器：

```bash
docker compose down
```

停止并删除卷（会清空数据库和文件）：

```bash
docker compose down -v
```

## 9. 常见问题

1. 前端登录报 `ECONNREFUSED`  
   先看 `docker compose logs -f api`，确认 API 已启动并迁移成功。

2. API 启动失败（JWT 密钥长度）  
   检查是否使用了 `docker compose --env-file .env.docker up -d --build`，且 `JWT_SIGNING_KEY` 至少 32 字符。

3. MySQL 启动后 API 仍连接失败  
   等待 `mysql` 健康检查通过，或查看 `docker compose logs -f mysql`。
