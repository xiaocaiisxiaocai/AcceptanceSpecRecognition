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
→ 记录停机态快照，必要时补做停机态备份
→ 单实例执行迁移门禁
→ 先启动并验证 API
→ 再启动并验证 Web
→ 比对数据快照并抽查历史业务数据
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
$MySqlInspect = docker inspect acceptance-mysql | ConvertFrom-Json
$ApiInspect = docker inspect acceptance-api | ConvertFrom-Json
$MySqlInspect[0].Mounts | ForEach-Object { "$($_.Name) -> $($_.Destination)" }
$ApiInspect[0].Mounts | ForEach-Object { "$($_.Name) -> $($_.Destination)" }
```

必须确认原有卷仍分别挂载到：

- `acceptancespecrecognition_mysql-data -> /var/lib/mysql`
- `acceptancespecrecognition_api-files -> /data/files`
- `acceptancespecrecognition_api-dpkeys -> /data/dp-keys`
- `acceptancespecrecognition_api-backups -> /app/backups`

挂载不符合预期时停止更新。这里不使用包含 `->` 的 `docker inspect --format`
模板；部分 Windows PowerShell 与 Docker CLI 组合会把它拆成原生命令参数。

### 步骤 3：生成本次数据库备份

创建仓库外备份目录：

```powershell
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$BackupDir = "D:\AcceptanceBackups"
$BackupFile = "$BackupDir\backup-before-update-$Stamp.sql"
New-Item -ItemType Directory -Force $BackupDir | Out-Null
```

先确认宿主机空间，再在 MySQL 容器内生成一致性备份。将长命令保存在变量中，
避免从聊天或文档复制时被换行拆断：

```powershell
Get-PSDrive D | Select-Object Name,@{Name='FreeGB';Expression={[math]::Round($_.Free / 1GB, 2)}}
docker exec acceptance-mysql rm -f /tmp/acceptance-predeploy.sql
$DumpCommand = 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction "$MYSQL_DATABASE" -r/tmp/acceptance-predeploy.sql'
docker exec acceptance-mysql sh -c $DumpCommand
$DumpExitCode = $LASTEXITCODE
"mysqldump_exit=$DumpExitCode"
if ($DumpExitCode -ne 0) { throw "数据库备份失败，禁止继续更新" }
```

出现 `Using a password on the command line interface can be insecure` 是 MySQL 客户端警告；
退出码为 `0` 时不代表失败。`>>` 表示 PowerShell 仍在等待命令结束，应按 `Ctrl+C`
取消后使用上面的变量写法重试。

检查并复制备份：

```powershell
docker exec acceptance-mysql ls -lh /tmp/acceptance-predeploy.sql
docker cp acceptance-mysql:/tmp/acceptance-predeploy.sql $BackupFile
$CopyExitCode = $LASTEXITCODE
"docker_cp_exit=$CopyExitCode"
if ($CopyExitCode -ne 0) { throw "备份复制失败，禁止继续更新" }
$BackupInfo = Get-Item $BackupFile
$BackupInfo | Select-Object FullName,Length,LastWriteTime
$BackupHash = Get-FileHash $BackupFile -Algorithm SHA256
$BackupHash | Select-Object Algorithm,Hash,Path
```

要求：

- `mysqldump` 退出码为 `0`。
- 容器文件和宿主机文件大小均大于 `0`。
- 保存 SHA256。
- 每次发布都必须生成新的备份并保存文件大小和 SHA256。
- 生产环境应另存一份离机副本，并定期在相同 MySQL 大版本的隔离容器中执行恢复演练。
- 如果本次存在破坏性或未分类迁移，必须对本次维护窗口生成的备份完成实际恢复验证；
  近期恢复演练不能替代本次备份的恢复验证，也不能据此声明 `--backup-verified`。

确认宿主机备份有效后，清理容器临时文件：

```powershell
docker exec acceptance-mysql rm -f /tmp/acceptance-predeploy.sql
$RemoveMysqlTempExit = $LASTEXITCODE
"remove_mysql_temp_exit=$RemoveMysqlTempExit"
Test-Path $BackupFile
```

### 步骤 4：保留当前运行镜像

在构建新镜像前记录当前容器镜像并添加回退标签：

```powershell
$OldApiImage = docker inspect acceptance-api --format '{{.Image}}'
$OldWebImage = docker inspect acceptance-web --format '{{.Image}}'
docker image tag $OldApiImage "acceptancespecrecognition-api:pre-$Stamp"
$ApiTagExit = $LASTEXITCODE
docker image tag $OldWebImage "acceptancespecrecognition-web:pre-$Stamp"
$WebTagExit = $LASTEXITCODE
"api_tag_exit=$ApiTagExit"
"web_tag_exit=$WebTagExit"
$RollbackApiImage = docker image inspect "acceptancespecrecognition-api:pre-$Stamp" --format '{{.Id}}'
$RollbackWebImage = docker image inspect "acceptancespecrecognition-web:pre-$Stamp" --format '{{.Id}}'
"api_rollback_image_matches=$($RollbackApiImage -eq $OldApiImage)"
"web_rollback_image_matches=$($RollbackWebImage -eq $OldWebImage)"
```

两个退出码必须为 `0`，两个匹配结果必须为 `True`。也可以额外列出标签：

```powershell
docker images acceptancespecrecognition-api
docker images acceptancespecrecognition-web
```

### 步骤 5：构建新镜像

旧系统继续运行时构建，减少停机时间：

```powershell
docker compose --env-file .env.docker build api
$ApiBuildExit = $LASTEXITCODE
"api_build_exit=$ApiBuildExit"
if ($ApiBuildExit -ne 0) { throw "API 镜像构建失败，禁止继续更新" }
docker compose --env-file .env.docker build web
$WebBuildExit = $LASTEXITCODE
"web_build_exit=$WebBuildExit"
if ($WebBuildExit -ne 0) { throw "Web 镜像构建失败，禁止继续更新" }
```

两个退出码都必须为 `0`。构建失败不会修改数据库，也不会替换正在运行的旧容器。
构建成功后确认新镜像已经生成，而运行容器仍保持旧镜像：

```powershell
$NewApiImage = docker image inspect acceptancespecrecognition-api:latest --format '{{.Id}}'
$NewWebImage = docker image inspect acceptancespecrecognition-web:latest --format '{{.Id}}'
$RunningApiImage = docker inspect acceptance-api --format '{{.Image}}'
$RunningWebImage = docker inspect acceptance-web --format '{{.Image}}'
"api_new_differs_from_old=$($NewApiImage -ne $OldApiImage)"
"web_new_differs_from_old=$($NewWebImage -ne $OldWebImage)"
"running_api_still_old=$($RunningApiImage -eq $OldApiImage)"
"running_web_still_old=$($RunningWebImage -eq $OldWebImage)"
```

四项都必须为 `True`。Compose 的构建进度可能同时列出依赖镜像；以服务构建退出码、
最终镜像 ID 和运行容器镜像 ID 为准。

### 步骤 6：只读检查结构模板唯一键冲突

通过标准输入传递 SQL，避免 PowerShell 与容器 Shell 的嵌套引号破坏命令：

```powershell
$ProductionMysql = 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"'
$TemplatePreflightSql = 'SELECT COUNT(*) AS DuplicateTemplateGroupCount FROM (SELECT CustomerId,HeadersFingerprint FROM DocumentTemplates GROUP BY CustomerId,HeadersFingerprint HAVING COUNT(*)>1) AS duplicates;'
$TemplatePreflightSql | docker exec -i acceptance-mysql sh -c $ProductionMysql
$TemplatePreflightExit = $LASTEXITCODE
"template_preflight_exit=$TemplatePreflightExit"
if ($TemplatePreflightExit -ne 0) { throw "结构模板预检失败，禁止继续更新" }
```

必须返回：

```text
DuplicateTemplateGroupCount
0
template_preflight_exit=0
```

存在重复行时停止部署，禁止手工删除数据后继续。

同时记录更新前的关键业务数据快照，发布完成后使用相同 SQL 比对：

```powershell
$DataSnapshotSql = 'SELECT (SELECT COUNT(*) FROM Customers) AS Customers,(SELECT COUNT(*) FROM AcceptanceSpecs) AS AcceptanceSpecs,(SELECT COUNT(*) FROM WordFiles) AS WordFiles,(SELECT COUNT(*) FROM ExecutionHistoryRecords) AS ExecutionHistoryRecords,(SELECT COUNT(*) FROM DocumentTemplates) AS DocumentTemplates,(SELECT COUNT(*) FROM SystemUsers) AS SystemUsers,(SELECT COUNT(*) FROM ColumnMappingRules) AS ColumnMappingRules,(SELECT COUNT(*) FROM __EFMigrationsHistory) AS Migrations;'
$BeforeDataSnapshot = @($DataSnapshotSql | docker exec -i acceptance-mysql sh -c $ProductionMysql)
$BeforeDataExit = $LASTEXITCODE
$BeforeDataSnapshot
"before_data_exit=$BeforeDataExit"
if ($BeforeDataExit -ne 0) { throw "业务数据快照读取失败，禁止继续更新" }
```

### 步骤 7：停止 Web 和 API

```powershell
docker compose --env-file .env.docker stop web api
$StopAppsExit = $LASTEXITCODE
$ApiState = docker inspect acceptance-api --format '{{.State.Status}}'
$WebState = docker inspect acceptance-web --format '{{.State.Status}}'
$MysqlState = docker inspect acceptance-mysql --format '{{.State.Status}}'
$MysqlHealth = docker inspect acceptance-mysql --format '{{.State.Health.Status}}'
"stop_apps_exit=$StopAppsExit"
"api_state=$ApiState"
"web_state=$WebState"
"mysql_state=$MysqlState"
"mysql_health=$MysqlHealth"
```

要求：

- `acceptance-web` 和 `acceptance-api` 已停止。
- `acceptance-mysql` 继续运行并保持 `healthy`。

停止业务写入后重新读取快照：

```powershell
$StoppedDataSnapshot = @($DataSnapshotSql | docker exec -i acceptance-mysql sh -c $ProductionMysql)
$StoppedSnapshotExit = $LASTEXITCODE
$StoppedDataSnapshot
$PreStopDataDiff = @(Compare-Object $BeforeDataSnapshot $StoppedDataSnapshot)
$PreStopDataDiff
"stopped_snapshot_exit=$StoppedSnapshotExit"
"pre_stop_data_diff_lines=$($PreStopDataDiff.Count)"
if ($StoppedSnapshotExit -ne 0) { throw "停机态数据快照读取失败，禁止执行迁移" }
```

在线备份和停机之间仍可能有请求完成。如果 `pre_stop_data_diff_lines` 大于 `0`，
说明在线备份之后数据发生变化，必须立即补做停机态备份并保留两份文件。即使行数
没有变化，也不能证明期间没有发生内容更新；如果步骤 8 检出破坏性或未分类迁移，
仍必须执行下面的命令补做停机态备份：

```powershell
$StoppedBackupStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$StoppedBackupFile = "$BackupDir\backup-stopped-state-$StoppedBackupStamp.sql"
$StoppedDumpCommand = 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction "$MYSQL_DATABASE" -r/tmp/acceptance-stopped-state.sql'
docker exec acceptance-mysql rm -f /tmp/acceptance-stopped-state.sql
docker exec acceptance-mysql sh -c $StoppedDumpCommand
$StoppedDumpExit = $LASTEXITCODE
"stopped_dump_exit=$StoppedDumpExit"
if ($StoppedDumpExit -ne 0) { throw "停机态数据库备份失败，禁止执行迁移" }
docker cp acceptance-mysql:/tmp/acceptance-stopped-state.sql $StoppedBackupFile
$StoppedCopyExit = $LASTEXITCODE
"stopped_copy_exit=$StoppedCopyExit"
if ($StoppedCopyExit -ne 0) { throw "停机态备份复制失败，禁止执行迁移" }
Get-Item $StoppedBackupFile | Select-Object FullName,Length,LastWriteTime
Get-FileHash $StoppedBackupFile -Algorithm SHA256
docker exec acceptance-mysql rm -f /tmp/acceptance-stopped-state.sql
```

### 步骤 8：单实例执行迁移门禁

先使用不带破坏性批准参数的迁移模式。无待执行迁移或只有安全迁移时，该命令完成后
退出，不启动 HTTP 服务：

```powershell
docker compose --env-file .env.docker run --rm --no-deps api --migrate-only
$MigrationApplyExit = $LASTEXITCODE
"migration_apply_exit=$MigrationApplyExit"
```

如果退出码为 `0`，表示迁移门禁已完成，不要再追加 `--backup-verified`。

只有日志明确列出“破坏性迁移”或“未分类迁移”时，才进入受控迁移分支。此时必须先
对本次维护窗口的最新备份执行隔离恢复验证；验证数据库能够启动、表清单和所有表
精确行数与停机态数据库一致。

受控迁移必须使用停止业务写入后生成的 `$StoppedBackupFile`。如果步骤 7 因行数
没有变化而尚未生成它，先返回执行步骤 7 的停机态备份命令；不得用在线备份替代：

```powershell
$VerifiedBackupFile = $StoppedBackupFile
Test-Path $VerifiedBackupFile
if (-not (Test-Path $VerifiedBackupFile)) { throw "缺少停机态备份，禁止批准受控迁移" }
```

在不接入生产网络、不挂载生产卷的临时 MySQL 容器中验证恢复：

```powershell
$RestoreContainer = "acceptance-restore-check-$Stamp"
$RestorePassword = [Guid]::NewGuid().ToString("N")
docker run -d --name $RestoreContainer --network none -e "MYSQL_ROOT_PASSWORD=$RestorePassword" mysql:8.0
if ($LASTEXITCODE -ne 0) { throw "恢复验证容器创建失败" }

$RestoreReady = $false
for ($i = 0; $i -lt 180; $i++) {
    docker exec $RestoreContainer sh -c 'mysqladmin ping -uroot -p"$MYSQL_ROOT_PASSWORD" --silent' *> $null
    if ($LASTEXITCODE -eq 0) {
        $RestoreReady = $true
        break
    }
    Start-Sleep -Seconds 2
}
"restore_container_ready=$RestoreReady"
if (-not $RestoreReady) { throw "恢复验证容器未在 6 分钟内就绪" }

'CREATE DATABASE restorecheck CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;' |
    docker exec -i $RestoreContainer sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD"'
if ($LASTEXITCODE -ne 0) { throw "恢复验证数据库创建失败" }

docker cp $VerifiedBackupFile "$($RestoreContainer):/tmp/acceptance-predeploy.sql"
if ($LASTEXITCODE -ne 0) { throw "恢复验证文件复制失败" }

docker exec $RestoreContainer sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" restorecheck < /tmp/acceptance-predeploy.sql'
$RestoreExitCode = $LASTEXITCODE
"restore_exit=$RestoreExitCode"
if ($RestoreExitCode -ne 0) { throw "备份恢复失败，禁止批准受控迁移" }
```

恢复大文件时前台命令可能长时间没有输出，不要重复执行或中断。可在另一个
PowerShell 窗口观察；`PROCESSLIST` 中存在针对 `restorecheck` 的工作语句，或
`BLOCK I/O` 持续增长，都说明仍在处理：

```powershell
docker stats --no-stream $RestoreContainer
'SELECT ID,DB,COMMAND,TIME,STATE FROM information_schema.PROCESSLIST;' |
    docker exec -i $RestoreContainer sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD"'
```

恢复命令返回且 `restore_exit=0` 后，逐表比对精确行数：

```powershell
function Get-DatabaseExactCounts {
    param([string]$Container,[string]$MysqlCommand)

    $Tables = 'SHOW TABLES;' | docker exec -i $Container sh -c $MysqlCommand
    if ($LASTEXITCODE -ne 0) { throw "读取 $Container 表清单失败" }

    foreach ($Table in $Tables) {
        if ($Table -notmatch '^[A-Za-z0-9_]+$') { throw "发现异常表名：$Table" }
        $Count = "SELECT COUNT(*) FROM $Table;" | docker exec -i $Container sh -c $MysqlCommand
        if ($LASTEXITCODE -ne 0) { throw "统计 $Container/$Table 失败" }
        "$Table=$Count"
    }
}

$ProductionMysqlNoHeader = 'export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; mysql -uroot -N "$MYSQL_DATABASE"'
$RestoreMysqlNoHeader = 'export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; mysql -uroot -N restorecheck'
$ProductionAllCounts = Get-DatabaseExactCounts acceptance-mysql $ProductionMysqlNoHeader | Sort-Object
$RestoreAllCounts = Get-DatabaseExactCounts $RestoreContainer $RestoreMysqlNoHeader | Sort-Object
$RestoreCountDiff = @(Compare-Object $ProductionAllCounts $RestoreAllCounts)
"production_table_lines=$(@($ProductionAllCounts).Count)"
"restore_table_lines=$(@($RestoreAllCounts).Count)"
"restore_count_diff_lines=$($RestoreCountDiff.Count)"
$RestoreCountDiff
if ($RestoreCountDiff.Count -ne 0) { throw "恢复库与停机态生产库行数不一致" }

$MigrationIdSql = 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;'
$ProductionMigrations = @($MigrationIdSql | docker exec -i acceptance-mysql sh -c $ProductionMysqlNoHeader)
if ($LASTEXITCODE -ne 0) { throw "读取生产迁移历史失败" }
$RestoreMigrations = @($MigrationIdSql | docker exec -i $RestoreContainer sh -c $RestoreMysqlNoHeader)
if ($LASTEXITCODE -ne 0) { throw "读取恢复库迁移历史失败" }
$RestoreMigrationDiff = @(Compare-Object $ProductionMigrations $RestoreMigrations)
"production_migrations=$($ProductionMigrations.Count)"
"restore_migrations=$($RestoreMigrations.Count)"
"restore_migration_diff_lines=$($RestoreMigrationDiff.Count)"
$RestoreMigrationDiff
if ($RestoreMigrationDiff.Count -ne 0) { throw "恢复库与生产库迁移历史不一致" }
```

只有 `restore_exit=0`、表数量一致、`restore_count_diff_lines=0` 且
`restore_migration_diff_lines=0` 时，才能声明备份恢复验证完成。验证完成后删除临时
容器；这不会删除宿主机上的 SQL 备份：

```powershell
docker rm -f $RestoreContainer
if ($LASTEXITCODE -ne 0) { throw "恢复验证临时容器清理失败" }
Test-Path $VerifiedBackupFile
```

然后才能执行：

```powershell
docker compose --env-file .env.docker run --rm --no-deps api --apply-destructive-migrations --backup-verified
$MigrationApplyExit = $LASTEXITCODE
"migration_apply_exit=$MigrationApplyExit"
```

连接失败、权限错误、磁盘不足或 SQL 异常不属于“需要破坏性批准”，不得通过追加
参数绕过。

两条迁移路径都要求：

- 只运行一个迁移容器。
- 不得同时启动 API 副本。
- 迁移过程不得中断。
- 完成日志应包含“数据库迁移命令已完成”。
- `migration_apply_exit` 必须为 `0`。

迁移期间出现慢查询警告可能是索引和历史数据处理产生的；最终退出码不为 `0`
时仍视为失败。

迁移成功后确认关键表和迁移历史没有非预期变化：

```powershell
$AfterMigrationSnapshot = @($DataSnapshotSql | docker exec -i acceptance-mysql sh -c $ProductionMysql)
$AfterMigrationExit = $LASTEXITCODE
$AfterMigrationSnapshot
$MigrationDataDiff = @(Compare-Object $StoppedDataSnapshot $AfterMigrationSnapshot)
$MigrationDataDiff
"after_migration_exit=$AfterMigrationExit"
"migration_data_diff_lines=$($MigrationDataDiff.Count)"
```

如果本次声明没有迁移或迁移设计不应改变这些表，`migration_data_diff_lines` 必须为
`0`。存在预期数据迁移时，应在发布说明中记录允许变化的表和数量，不能机械要求为零。
迁移失败时禁止启动新版本、禁止手工写迁移历史，应保留完整日志并检查数据库状态。

### 步骤 9：先启动和验证 API

```powershell
docker compose --env-file .env.docker up -d --no-deps api
$ApiStartExit = $LASTEXITCODE
"api_start_exit=$ApiStartExit"
if ($ApiStartExit -ne 0) { throw "API 启动失败，禁止启动 Web" }

$ApiImageAfterStart = docker inspect acceptance-api --format '{{.Image}}'
"api_uses_new_image=$($ApiImageAfterStart -eq $NewApiImage)"
if ($ApiImageAfterStart -ne $NewApiImage) { throw "API 未使用本次构建的新镜像" }

$ApiHealthy = $false
for ($i = 0; $i -lt 30; $i++) {
    $ApiHealth = docker inspect acceptance-api --format '{{.State.Health.Status}}'
    if ($ApiHealth -eq 'healthy') {
        $ApiHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
}
"api_healthy=$ApiHealthy"
if (-not $ApiHealthy) { throw "API 未在 60 秒内进入 healthy，禁止启动 Web" }
docker compose --env-file .env.docker ps
docker compose --env-file .env.docker logs --tail 100 api
```

容器健康后验证 API 就绪端点：

```powershell
$ReadyResponse = curl.exe --fail --silent --show-error --max-time 15 http://127.0.0.1:5290/health/ready
$ReadyExit = $LASTEXITCODE
"ready_exit=$ReadyExit"
$ReadyResponse
if ($ReadyExit -ne 0) { throw "API 就绪检查失败，禁止启动 Web" }
```

必须确认：

- `api_start_exit=0`、`api_uses_new_image=True`、`api_healthy=True`。
- `ready_exit=0`，响应顶层 `status` 为 `Healthy`。
- API 容器为 `healthy`。
- `database` 为 `Healthy`。
- `migrations` 为 `Healthy`。
- `pendingDestructiveMigrationIds` 为空。
- `singleCompany` 为 `Healthy`。

API 不健康时禁止启动 Web。

### 步骤 10：启动和验证 Web

```powershell
docker compose --env-file .env.docker up -d --no-deps web
$WebStartExit = $LASTEXITCODE
"web_start_exit=$WebStartExit"
if ($WebStartExit -ne 0) { throw "Web 启动失败" }

$WebImageAfterStart = docker inspect acceptance-web --format '{{.Image}}'
"web_uses_new_image=$($WebImageAfterStart -eq $NewWebImage)"
if ($WebImageAfterStart -ne $NewWebImage) { throw "Web 未使用本次构建的新镜像" }

$WebHealthy = $false
for ($i = 0; $i -lt 30; $i++) {
    $WebHealth = docker inspect acceptance-web --format '{{.State.Health.Status}}'
    if ($WebHealth -eq 'healthy') {
        $WebHealthy = $true
        break
    }
    Start-Sleep -Seconds 2
}
"web_healthy=$WebHealthy"
if (-not $WebHealthy) { throw "Web 未在 60 秒内进入 healthy" }
docker compose --env-file .env.docker ps
curl.exe --fail -I --max-time 15 http://127.0.0.1/
$WebReadyExit = $LASTEXITCODE
"web_ready_exit=$WebReadyExit"
if ($WebReadyExit -ne 0) { throw "Web 首页检查失败" }
docker compose --env-file .env.docker logs --tail 50 web
```

Web 刚启动时可能短暂显示 `health: starting`。最终必须满足
`web_start_exit=0`、`web_uses_new_image=True`、`web_healthy=True`、
`web_ready_exit=0`，且首页返回：

```text
HTTP/1.1 200 OK
```

不要使用 `GET /login` 判断前端健康。`/login` 是认证接口，错误方法或未认证请求返回
`401 Unauthorized` 不代表前端启动失败。

### 步骤 11：业务数据验收

先在恢复业务操作前读取最终快照，并与停机态快照比对：

```powershell
$FinalDataSnapshot = @($DataSnapshotSql | docker exec -i acceptance-mysql sh -c $ProductionMysql)
$FinalDataExit = $LASTEXITCODE
$FinalDataSnapshot
$FinalDataDiff = @(Compare-Object $StoppedDataSnapshot $FinalDataSnapshot)
$FinalDataDiff
"final_data_exit=$FinalDataExit"
"final_data_diff_lines=$($FinalDataDiff.Count)"
if ($FinalDataExit -ne 0) { throw "最终业务数据快照读取失败" }
```

如果本次迁移不应改变这些表，且比对期间尚未恢复业务写入，
`final_data_diff_lines` 必须为 `0`。存在已审批的数据迁移时，按发布说明核对允许变化。

记录最终代码和镜像证据：

```powershell
$FinalGitSha = git rev-parse HEAD
$FinalApiImage = docker inspect acceptance-api --format '{{.Image}}'
$FinalWebImage = docker inspect acceptance-web --format '{{.Image}}'
"final_git_sha=$FinalGitSha"
"final_api_image=$FinalApiImage"
"final_web_image=$FinalWebImage"
"final_api_matches_build=$($FinalApiImage -eq $NewApiImage)"
"final_web_matches_build=$($FinalWebImage -eq $NewWebImage)"
```

完整 Git SHA 必须等于本次计划发布 SHA，两个镜像匹配结果必须为 `True`。

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
3. Shell 命令较长时先保存到 PowerShell 字符串变量，再传给 `sh -c`。
4. SQL 较长时用管道传给 `docker exec -i`，不要把 SQL 和数据库名拆到两行。

例如：

```powershell
$MysqlCommand = 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD" "$MYSQL_DATABASE"'
$Sql = 'SELECT COUNT(*) FROM Customers;'
$Sql | docker exec -i acceptance-mysql sh -c $MysqlCommand
```

### `docker inspect --format` 报 `unknown shorthand flag: '>' in ->`

这是 Windows PowerShell 与 Docker CLI 对格式字符串的参数解析兼容问题，不代表容器
或挂载损坏。使用 JSON 结果检查挂载：

```powershell
$Inspect = docker inspect acceptance-mysql | ConvertFrom-Json
$Inspect[0].Mounts | ForEach-Object { "$($_.Name) -> $($_.Destination)" }
```

### 恢复 SQL 十几分钟没有输出

`mysql < backup.sql` 默认不显示进度，大备份恢复十几分钟并不一定异常。不要在原窗口
重复执行。使用步骤 8 的 `docker stats` 和 `PROCESSLIST` 从另一个窗口观察；只有容器
退出、MySQL 报错或长时间完全没有磁盘活动时才进一步排查。

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
$MySqlInspect = docker inspect acceptance-mysql | ConvertFrom-Json
$MySqlInspect[0].Mounts | ForEach-Object { "$($_.Name) -> $($_.Destination)" }
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
- [ ] 已记录停机态数据快照；在线备份后若数据变化，已补做停机态备份。
- [ ] 旧 API/Web 镜像已有 `pre-$Stamp` 标签。
- [ ] API/Web 新镜像构建退出码均为 `0`。
- [ ] 新镜像 ID 与旧镜像不同，构建期间运行容器仍使用旧镜像。
- [ ] 结构模板重复指纹数量为 `0`。
- [ ] 已先执行 `--migrate-only`；只有明确受控迁移时才进入批准分支。
- [ ] 如存在破坏性或未分类迁移，已补做停机态备份并完成隔离恢复，所有表行数和迁移历史完全一致。
- [ ] 迁移期间只有一个迁移容器。
- [ ] 迁移退出码为 `0`。
- [ ] 迁移前后数据快照无非预期差异。
- [ ] API `/health/ready` 为 `Healthy`。
- [ ] API/Web 运行镜像 ID 与本次构建镜像 ID 一致。
- [ ] Web 首页返回 `200 OK`。
- [ ] 三个容器最终均为 `healthy`。
- [ ] 已复核最终完整 Git SHA 和最终数据快照。
- [ ] 客户、制程、机型、验收规格和附件历史数据已抽查。
- [ ] 备份和回退镜像继续保留。

## 八、相关文件

- `docker-compose.yml`
- `.env.docker.example`
- `src/AcceptanceSpecSystem.Api/Dockerfile`
- `web/Dockerfile`
- `deploy/nginx/default.conf`
- `docs/DEPLOY-DOCKER.md`
