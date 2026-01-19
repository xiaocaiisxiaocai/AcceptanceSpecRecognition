# 部署说明（Docker Compose）

## 1. 前置条件

- 安装 Docker Desktop（Windows/macOS）或 Docker Engine（Linux）
- 需要可用的 80/8080、5014、3306 端口（可在 `docker-compose.yml` 中调整映射）

## 2. 一键启动（推荐）

在仓库根目录：

```powershell
docker compose up -d --build
```

查看状态：

```powershell
docker compose ps
```

日志：

```powershell
docker compose logs -f api
docker compose logs -f web
docker compose logs -f mysql
```

## 3. 访问地址

- **前端**：`http://localhost:8080`
- **后端 API**：`http://localhost:5014`
- **Swagger**：`http://localhost:8080/swagger`（同源反代）或 `http://localhost:5014/swagger`

## 4. 数据与文件持久化

`docker-compose.yml` 默认使用 volume：

- `mysql_data`：MySQL 数据文件
- `api_uploads`：上传/填充生成的 docx（对应容器内 `/app/uploads`）

## 5. 配置项（环境变量）

可在启动时覆盖（PowerShell 示例）：

```powershell
$env:MYSQL_ROOT_PASSWORD="yourRootPwd"
$env:MYSQL_DATABASE="acceptance_spec_db"
$env:MYSQL_USER="app"
$env:MYSQL_PASSWORD="app123"
docker compose up -d --build
```

后端连接串通过 `ConnectionStrings__DefaultConnection` 注入（compose 已默认设置为连接 `mysql` 服务）。

## 6. 停止与清理

停止：

```powershell
docker compose down
```

连同数据卷一起删除（危险，会清空数据/上传文件）：

```powershell
docker compose down -v
```

