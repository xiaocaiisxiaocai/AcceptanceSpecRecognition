[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$VersionTag,

  [string]$Platform = "linux/amd64",

  [string]$ApiImageName = "acceptance-api",

  [string]$WebImageName = "acceptance-web",

  [string]$ServerDeployDir = "/home/ubuntu/apps/acceptance-spec-system/image-deploy",

  [string]$OutputDir,

  [switch]$SkipBuild,

  [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Resolve-DockerCommand {
  $dockerCommand = Get-Command docker -ErrorAction SilentlyContinue
  if ($dockerCommand) {
    return $dockerCommand.Source
  }

  $candidates = @(
    "C:\Program Files\Docker\Docker\resources\bin\docker.exe",
    "C:\ProgramData\DockerDesktop\version-bin\docker.exe"
  )

  foreach ($candidate in $candidates) {
    if (Test-Path $candidate) {
      return $candidate
    }
  }

  throw "未找到 docker 命令，请先安装 Docker Desktop 或把 docker 加入 PATH。"
}

function Invoke-Step {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Title,

    [Parameter(Mandatory = $true)]
    [scriptblock]$Action
  )

  Write-Host ""
  Write-Host "==> $Title" -ForegroundColor Cyan
  & $Action
}

function Invoke-ExternalCommand {
  param(
    [Parameter(Mandatory = $true)]
    [string]$FilePath,

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
  )

  & $FilePath @Arguments
  if ($LASTEXITCODE -ne 0) {
    $joinedArguments = ($Arguments -join " ").Trim()
    throw "外部命令执行失败: $FilePath $joinedArguments"
  }
}

function New-TextFile {
  param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$Content
  )

  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$docker = Resolve-DockerCommand
$repoRoot = Split-Path -Parent $PSScriptRoot
$apiDockerfile = Join-Path $repoRoot "src\AcceptanceSpecSystem.Api\Dockerfile"
$webDockerfile = Join-Path $repoRoot "web\Dockerfile"
$composeTemplate = Join-Path $PSScriptRoot "docker-compose.images.yml"
$envTemplate = Join-Path $PSScriptRoot "production.env.example"
$envValidatorTemplate = Join-Path $repoRoot "deploy\validate-production-env.sh"
$collationPreflightTemplate = Join-Path $repoRoot "deploy\preflight-collation-migration.sql"

if (-not (Test-Path $apiDockerfile)) {
  throw "未找到 API Dockerfile: $apiDockerfile"
}

if (-not (Test-Path $webDockerfile)) {
  throw "未找到 Web Dockerfile: $webDockerfile"
}

if (-not (Test-Path $composeTemplate)) {
  throw "未找到部署模板: $composeTemplate"
}

if (-not (Test-Path $envTemplate)) {
  throw "未找到环境变量模板: $envTemplate"
}

if (-not (Test-Path $envValidatorTemplate)) {
  throw "未找到生产环境变量校验脚本: $envValidatorTemplate"
}

if (-not (Test-Path $collationPreflightTemplate)) {
  throw "未找到排序规则迁移预检脚本: $collationPreflightTemplate"
}

$releaseRoot = if ($OutputDir) {
  $OutputDir
} else {
  Join-Path $repoRoot ".tmpbuild\releases\$VersionTag"
}

$releaseRoot = [System.IO.Path]::GetFullPath($releaseRoot)
$apiImage = "${ApiImageName}:$VersionTag"
$webImage = "${WebImageName}:$VersionTag"
$apiTarName = "$ApiImageName-$VersionTag.tar"
$webTarName = "$WebImageName-$VersionTag.tar"
$apiTarPath = Join-Path $releaseRoot $apiTarName
$webTarPath = Join-Path $releaseRoot $webTarName
$releaseComposePath = Join-Path $releaseRoot "docker-compose.yml"
$releaseEnvExamplePath = Join-Path $releaseRoot "production.env.example"
$releaseEnvValidatorPath = Join-Path $releaseRoot "validate-production-env.sh"
$releaseCollationPreflightPath = Join-Path $releaseRoot "preflight-collation-migration.sql"
$serverDeployGuidePath = Join-Path $releaseRoot "SERVER-DEPLOY.txt"

if ((Test-Path $releaseRoot) -and $Force) {
  Remove-Item -Recurse -Force $releaseRoot
}

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

Invoke-Step -Title "检查 Docker 环境" -Action {
  Invoke-ExternalCommand $docker "--version"
  Invoke-ExternalCommand $docker "info" "--format" "{{.ServerVersion}}"
}

if (-not $SkipBuild) {
  Invoke-Step -Title "构建 API 镜像 $apiImage" -Action {
    Invoke-ExternalCommand $docker "build" `
      --platform $Platform `
      -f $apiDockerfile `
      -t $apiImage `
      $repoRoot
  }

  Invoke-Step -Title "构建 Web 镜像 $webImage" -Action {
    Invoke-ExternalCommand $docker "build" `
      --platform $Platform `
      -f $webDockerfile `
      -t $webImage `
      $repoRoot
  }
}

Invoke-Step -Title "导出镜像 tar" -Action {
  Invoke-ExternalCommand $docker "save" "-o" $apiTarPath $apiImage
  Invoke-ExternalCommand $docker "save" "-o" $webTarPath $webImage
}

Invoke-Step -Title "生成发布目录文件" -Action {
  Copy-Item -Force $composeTemplate $releaseComposePath
  Copy-Item -Force $envTemplate $releaseEnvExamplePath
  Copy-Item -Force $envValidatorTemplate $releaseEnvValidatorPath
  Copy-Item -Force $collationPreflightTemplate $releaseCollationPreflightPath

  $guide = @"
发布版本：$VersionTag

上传目标目录：
$ServerDeployDir

需要上传的文件：
- docker-compose.yml
- $apiTarName
- $webTarName
- production.env.example（仅首次部署参考，不要覆盖线上现有 .env）
- validate-production-env.sh
- preflight-collation-migration.sql

服务器执行命令：
cd $ServerDeployDir
sudo docker load -i $apiTarName
sudo docker load -i $webTarName
sh validate-production-env.sh .env

已有数据库首次升级到本版本时，在启动新 API 前：
1. 停止全部 API 副本并完成备份/恢复抽检。
2. 执行 preflight-collation-migration.sql，确认没有活动事务、锁等待、唯一键冲突，并核对可用磁盘空间。
3. 备份恢复验证完成后，只运行一个 `docker compose run --rm --no-deps api --apply-destructive-migrations --backup-verified`。
4. 成功后再执行下面的 up -d；失败时排除原因后重复 migrate-only，禁止跳过迁移强行启动。

sed -i 's#^API_IMAGE=.*#API_IMAGE=$apiImage#' .env
sed -i 's#^WEB_IMAGE=.*#WEB_IMAGE=$webImage#' .env
sudo docker compose --env-file .env -f docker-compose.yml up -d
sudo docker compose --env-file .env -f docker-compose.yml ps

校验脚本只输出不合格的变量名，不会回显任何密钥值；两个初始化密码至少 12 位，JWT 密钥至少 32 位。
数据库备份保存在 api-backups 卷中。请定期复制到异机或对象存储，生产环境不要执行 docker compose down -v。

验证命令：
curl http://127.0.0.1:15290/health
curl -I http://127.0.0.1:18080
"@

  New-TextFile -Path $serverDeployGuidePath -Content $guide
}

$apiTarSizeMb = [Math]::Round(((Get-Item $apiTarPath).Length / 1MB), 2)
$webTarSizeMb = [Math]::Round(((Get-Item $webTarPath).Length / 1MB), 2)

Write-Host ""
Write-Host "发布包已生成：" -ForegroundColor Green
Write-Host "  目录: $releaseRoot"
Write-Host "  API:  $apiTarName (${apiTarSizeMb} MB)"
Write-Host "  Web:  $webTarName (${webTarSizeMb} MB)"
Write-Host "  Compose: docker-compose.yml"
Write-Host "  指南: SERVER-DEPLOY.txt"
