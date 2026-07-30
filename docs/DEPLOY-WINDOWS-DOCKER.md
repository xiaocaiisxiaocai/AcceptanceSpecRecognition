# Windows Docker 部署与生产更新指南

本文是本项目在 Windows 主机上使用 Docker Compose 部署和更新的统一操作手册。

适用组件：

- `acceptance-web`
- `acceptance-api`
- `acceptance-mysql`

默认配置：

- 代码分支：`main`
- 环境文件：`.env.docker`
- Web 端口：`80`
- API 本机排障端口：`5290`
- MySQL：仅容器网络访问

## 一、生产安全红线

生产环境禁止执行：

```powershell
docker compose down -v
docker volume rm ...
docker system prune --volumes
docker compose rm mysql
```

同时禁止：

- 删除或重建 `acceptance-mysql`。
- 删除 MySQL 的 `/var/lib/mysql` 持久化卷。
- 从新目录启动同一套生产 Compose。
- 用 `.env.docker.example` 覆盖生产 `.env.docker`。
- 将真实密码、JWT 密钥或生产 `.env.docker` 提交到 Git。
- 手工修改 `__EFMigrationsHistory`。
- 迁移失败后强行启动新版本。
- 未确认影响时把数据库备份恢复并覆盖生产库。

生产更新必须始终在原部署目录执行。Compose 的卷名与项目目录或项目名有关；
换目录启动可能创建一套新的空卷，使系统看起来像“数据丢失”。

## 二、首次部署

### 1. 检查环境

```powershell
docker --version
docker compose version
git --version
```

### 2. 克隆代码

```powershell
Set-Location D:\project
git clone https://github.com/xiaocaiisxiaocai/AcceptanceSpecRecognition.git
Set-Location .\AcceptanceSpecRecognition
git switch main
git pull --ff-only origin main
git rev-parse HEAD
```

### 3. 创建生产配置

复制示例文件：

```powershell
Copy-Item .env.docker.example .env.docker
```

填写 `.env.docker` 中的真实配置。不得保留示例密码或占位符；不得在聊天、
截图、日志或 Git 中公开配置值。

内网同站 HTTP 部署至少需要：

```env
CORS_ORIGIN_0=http://实际固定内网主机名或IP
CORS_ORIGIN_1=
BROWSER_AUTH_ALLOW_INSECURE_HTTP=true
BROWSER_AUTH_REFRESH_COOKIE_NAME=acceptance-refresh
BROWSER_AUTH_COOKIE_SECURE=false
BROWSER_AUTH_COOKIE_SAME_SITE=Strict
BROWSER_AUTH_COOKIE_DOMAIN=
```

HTTP 只适用于受信任的隔离内网，不得暴露到互联网或不可信无线网络。

### 4. 首次构建和启动

```powershell
docker compose --env-file .env.docker build api
docker compose --env-file .env.docker build web
docker compose --env-file .env.docker up -d
docker compose --env-file .env.docker ps
```

首次空库会自动建立最终数据库结构。已有数据库升级必须使用后文的生产更新流程。

### 5. 首次验证

```powershell
curl.exe --max-time 15 http://127.0.0.1:5290/health/ready
curl.exe -I --max-time 15 http://127.0.0.1/
```

预期：

- API 返回 `"status":"Healthy"`。
- Web 返回 `HTTP/1.1 200 OK`。
- `docker compose ps` 中三个容器均为 `healthy`。

## 三、生产更新最短流程

每次更新必须按以下顺序执行：

```text
拉取并核对版本
→ 备份数据库
→ 保留旧镜像
→ 构建新镜像
→ 只读数据预检
→ 停止 Web/API
→ 单实例执行受控迁移
→ 先启动并验证 API
→ 再启动并验证 Web
→ 抽查历史业务数据
```

不要从构建、迁移或启动步骤中途开始。

## 四、生产更新详细步骤

以下命令默认在原生产目录执行：

```powershell
Set-Location D:\project\AcceptanceSpecRecognition
```

如果实际目录不同，只替换这一行；后续不得切换到新的 Compose 目录。

### 步骤 1：拉取并核对最新代码

先确认工作区：

```powershell
git status --short
```

如果有输出，停止更新并先确认这些本地修改，禁止使用 `git reset --hard` 清理。

工作区干净时执行：

```powershell
git fetch origin
git switch main
git pull --ff-only origin main
git rev-parse HEAD
```

记录完整 SHA，确保它是本次计划发布的版本。

### 步骤 2：确认原持久化卷

```powershell
docker compose --env-file .env.docker ps
docker inspect acceptance-mysql --format '{{range .Mounts}}{{println .Name " -> " .Destination}}{{end}}'
docker inspect acceptance-api --format '{{range .Mounts}}{{println .Name " -> " .Destination}}{{end}}'
```

必须确认原有卷仍分别挂载到：

- `/var/lib/mysql`
- `/data/files`
- `/data/dp-keys`
- `/app/backups`

挂载不符合预期时停止更新。

### 步骤 3：生成本次数据库备份

创建仓库外备份目录：

```powershell
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupDir = "D:\AcceptanceBackups"
$BackupFile = "$BackupDir\backup-before-update-$Stamp.sql"
New-Item -ItemType Directory -Force $BackupDir | Out-Null
```

在 MySQL 容器内生成一致性备份。下面是一整行命令，中途不要回车：

```powershell
docker exec acceptance-mysql sh -c 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction "$MYSQL_DATABASE" -r/tmp/acceptance-predeploy.sql'
```

出现 `Using a password on the command line interface can be insecure` 是 MySQL 客户端警告；
退出码为 `0` 时不代表失败。

检查并复制备份：

```powershell
$LASTEXITCODE
docker exec acceptance-mysql ls -lh /tmp/acceptance-predeploy.sql
docker cp acceptance-mysql:/tmp/acceptance-predeploy.sql $BackupFile
Get-Item $BackupFile | Select-Object FullName,Length,LastWriteTime
Get-FileHash $BackupFile -Algorithm SHA256
```

要求：

- `mysqldump` 退出码为 `0`。
- 容器文件和宿主机文件大小均大于 `0`。
- 保存 SHA256。
- 生产环境应另存一份离机副本，并定期在隔离 MySQL 中进行恢复验证。

确认宿主机备份有效后，清理容器临时文件：

```powershell
docker exec acceptance-mysql rm -f /tmp/acceptance-predeploy.sql
```

### 步骤 4：保留当前运行镜像

在构建新镜像前记录当前容器镜像并添加回退标签：

```powershell
$OldApiImage = docker inspect acceptance-api --format '{{.Image}}'
$OldWebImage = docker inspect acceptance-web --format '{{.Image}}'
docker image tag $OldApiImage "acceptancespecrecognition-api:pre-$Stamp"
docker image tag $OldWebImage "acceptancespecrecognition-web:pre-$Stamp"
```

检查标签：

```powershell
docker images acceptancespecrecognition-api
docker images acceptancespecrecognition-web
```

### 步骤 5：构建新镜像

旧系统继续运行时构建，减少停机时间：

```powershell
docker compose --env-file .env.docker build api
$LASTEXITCODE
docker compose --env-file .env.docker build web
$LASTEXITCODE
```

两个退出码都必须为 `0`。构建失败不会修改数据库，也不会替换正在运行的旧容器。

### 步骤 6：只读检查结构模板唯一键冲突

进入 MySQL 交互终端，避免 Windows PowerShell 嵌套引号破坏 SQL：

```powershell
docker exec -it acceptance-mysql mysql -uroot -p
```

根据 `.env.docker` 中的 `MYSQL_DATABASE` 选择数据库：

```sql
USE 生产数据库名;
```

执行：

```sql
SELECT CustomerId, HeadersFingerprint, COUNT(*) AS DuplicateCount
FROM DocumentTemplates
GROUP BY CustomerId, HeadersFingerprint
HAVING COUNT(*) > 1;
```

必须返回：

```text
Empty set
```

存在重复行时停止部署，禁止手工删除数据后继续。

退出：

```sql
exit
```

### 步骤 7：停止 Web 和 API

```powershell
docker compose --env-file .env.docker stop web api
docker compose --env-file .env.docker ps
```

要求：

- `acceptance-web` 和 `acceptance-api` 已停止。
- `acceptance-mysql` 继续运行并保持 `healthy`。

### 步骤 8：单实例执行受控迁移

只有在已完成备份及恢复验证后，才允许声明 `--backup-verified`：

```powershell
docker compose --env-file .env.docker run --rm --no-deps api --apply-destructive-migrations --backup-verified
```

要求：

- 只运行一个迁移容器。
- 不得同时启动 API 副本。
- 迁移过程不得中断。
- 完成日志应包含“数据库迁移命令已完成”。
- `$LASTEXITCODE` 必须为 `0`。

迁移期间出现慢查询警告可能是索引和历史数据处理产生的；最终退出码不为 `0`
时仍视为失败。

迁移失败时禁止启动新版本、禁止手工写迁移历史，应保留完整日志并检查数据库状态。

### 步骤 9：先启动和验证 API

```powershell
docker compose --env-file .env.docker up -d --no-deps api
docker compose --env-file .env.docker ps
docker compose --env-file .env.docker logs --tail 100 api
```

验证：

```powershell
curl.exe --max-time 15 http://127.0.0.1:5290/health/ready
```

必须确认：

- API 容器为 `healthy`。
- `database` 为 `Healthy`。
- `migrations` 为 `Healthy`。
- `pendingDestructiveMigrationIds` 为空。
- `singleCompany` 为 `Healthy`。

API 不健康时禁止启动 Web。

### 步骤 10：启动和验证 Web

```powershell
docker compose --env-file .env.docker up -d --no-deps web
docker compose --env-file .env.docker ps
curl.exe -I --max-time 15 http://127.0.0.1/
```

Web 刚启动时可能短暂显示 `health: starting`。最终应为 `healthy`，首页应返回：

```text
HTTP/1.1 200 OK
```

不要使用 `GET /login` 判断前端健康。`/login` 是认证接口，错误方法或未认证请求返回
`401 Unauthorized` 不代表前端启动失败。

### 步骤 11：业务数据验收

浏览器登录后至少抽查：

- 客户
- 制程
- 机型
- 验收规格
- 结构模板
- 系统用户、角色和组织
- 上传文件
- 导入记录

历史数据、附件和权限均正常后，部署才算完成。保留本次 SQL 备份和回退镜像，
不要立即清理。

## 五、失败处理与回退

### 1. 拉取失败

不要使用 `reset --hard` 或强制覆盖。保留 `git status`、当前 SHA 和错误输出后处理。

### 2. 构建失败

旧容器仍在运行，不需要操作数据库。先修复源代码或构建环境，再重新构建。

### 3. 迁移失败

不要启动新 API，不要修改 `__EFMigrationsHistory`，不要立即恢复数据库覆盖生产。
先判断迁移是否产生部分 DDL，再决定前向修复或经批准恢复备份。

### 4. 应用启动失败

先检查：

```powershell
docker compose --env-file .env.docker ps
docker compose --env-file .env.docker logs --tail 200 api
docker compose --env-file .env.docker logs --tail 200 web
```

应用镜像回退不等于数据库回退。只有确认旧应用兼容迁移后的数据库时，才可把
`pre-$Stamp` 镜像重新标记为 `latest` 并重建对应应用容器。

数据库恢复会覆盖恢复点之后产生的新业务数据，必须单独审批并在停写状态下执行。

## 六、常见问题

### PowerShell 出现 `>>`

`>>` 表示命令或引号尚未结束，通常是复制时把容器命令拆成了多行。

处理方式：

1. 按 `Ctrl+C` 取消当前命令。
2. 重新复制文档中的单行命令。
3. SQL 较长时进入 MySQL 交互终端执行。

### `Using a password on the command line interface can be insecure`

这是 MySQL 客户端警告。以命令退出码、备份文件大小和 SHA256 判断备份结果。
不要把包含密码或环境变量的完整输出发到聊天、工单或截图中。

### `wwwroot` 不存在

Docker 部署中前端由独立 Nginx Web 容器提供，API 镜像没有 `/app/wwwroot`
通常不影响运行。

### Web 显示 `health: starting`

容器刚启动时属于正常状态。等待健康检查完成，再确认是否变为 `healthy`。

### 页面打开但历史数据为空

立即停止写入并检查：

```powershell
docker inspect acceptance-mysql --format '{{range .Mounts}}{{println .Name " -> " .Destination}}{{end}}'
docker volume ls
docker compose ls
```

常见原因是从新目录或不同 Compose 项目名启动，挂载了新的空卷。不要初始化空库、
不要删除原卷、不要把备份覆盖到未确认的目标。

### 端口占用

```powershell
netstat -ano | findstr :80
netstat -ano | findstr :5290
```

生产环境不得未经确认结束未知进程。先识别 PID 对应的服务及影响。

## 七、部署完成检查表

- [ ] 从原部署目录开始。
- [ ] `main` 已使用 `git pull --ff-only origin main` 拉取。
- [ ] 已记录发布 SHA。
- [ ] 原 MySQL、文件、密钥和备份卷挂载正常。
- [ ] 新 SQL 备份已复制到仓库外并记录 SHA256。
- [ ] 旧 API/Web 镜像已有 `pre-$Stamp` 标签。
- [ ] API/Web 新镜像构建退出码均为 `0`。
- [ ] 结构模板重复指纹查询为 `Empty set`。
- [ ] 迁移期间只有一个迁移容器。
- [ ] 迁移退出码为 `0`。
- [ ] API `/health/ready` 为 `Healthy`。
- [ ] Web 首页返回 `200 OK`。
- [ ] 三个容器最终均为 `healthy`。
- [ ] 客户、制程、机型、验收规格和附件历史数据已抽查。
- [ ] 备份和回退镜像继续保留。

## 八、相关文件

- `docker-compose.yml`
- `.env.docker.example`
- `src/AcceptanceSpecSystem.Api/Dockerfile`
- `web/Dockerfile`
- `deploy/nginx/default.conf`
- `docs/DEPLOY-DOCKER.md`
