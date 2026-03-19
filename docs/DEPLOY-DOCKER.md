# Docker 部署指南

本文提供本项目的 Docker 单机部署方案（`web + api + mysql`）。

## 1. 目录与文件

已提供如下文件：

- `docker-compose.yml`
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
docker compose up -d --build
```

查看状态：

```bash
docker compose ps
docker compose logs -f api
```

## 4. 访问地址

- 前端：`http://localhost:8080`
- API（直连）：`http://localhost:5290`
- API 健康检查：`http://localhost:5290/health`
- Swagger：`http://localhost:8080/swagger`

说明：

- 前端通过 Nginx 反向代理到 API。
- 反向代理已包含：`/api`、`/login`、`/refresh-token`、`/get-async-routes`。

## 5. 默认容器与端口

- `acceptance-web`: `8080 -> 80`
- `acceptance-api`: `5290 -> 8080`
- `acceptance-mysql`: 仅容器内访问（未映射宿主机端口）

## 6. 持久化卷

`docker-compose.yml` 中已配置：

- `mysql-data`：MySQL 数据
- `api-files`：上传文件与生成文件（`FileStorage`）
- `api-dpkeys`：DataProtection key ring

## 7. 关键环境变量（API）

在 `docker-compose.yml` 的 `api.environment` 中可按需调整：

- `ConnectionStrings__DefaultConnection`
- `JwtAuth__SigningKey`（建议替换为更长随机密钥）
- `FileStorage__BasePath`
- `DataProtection__KeysPath`
- `Cors__AllowedOrigins__0`

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
   `JwtAuth__SigningKey` 至少 32 字符。

3. MySQL 启动后 API 仍连接失败  
   等待 `mysql` 健康检查通过，或查看 `docker compose logs -f mysql`。
