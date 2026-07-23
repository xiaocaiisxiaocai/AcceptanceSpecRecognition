# Docker 镜像发布最短手册

当前服务器运行目录：

- `/home/ubuntu/apps/acceptance-spec-system/image-deploy`

当前线上端口：

- 前端：`18080`
- API：`15290`

## 0. 一键生成发布包

在项目根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\.deploy\Publish-DockerImageRelease.ps1 -VersionTag 20260319
```

执行完成后，会在下面目录生成发布包：

```text
.tmpbuild/releases/20260319/
├─ docker-compose.yml
├─ production.env.example
├─ validate-production-env.sh
├─ acceptance-api-20260319.tar
├─ acceptance-web-20260319.tar
└─ SERVER-DEPLOY.txt
```

常用参数：

- `-VersionTag 20260319`：发布版本号
- `-SkipBuild`：跳过构建，直接导出本地已有同标签镜像
- `-Force`：覆盖已有输出目录
- `-OutputDir D:\temp\release`：自定义输出目录

## 1. 本地构建镜像

在项目根目录执行：

```powershell
docker build --platform linux/amd64 -f src/AcceptanceSpecSystem.Api/Dockerfile -t acceptance-api:20260319 .
docker build --platform linux/amd64 -f web/Dockerfile -t acceptance-web:20260319 .
```

建议镜像标签使用发布日期，例如：`20260319`。

## 2. 本地导出镜像

```powershell
docker save -o acceptance-api-20260319.tar acceptance-api:20260319
docker save -o acceptance-web-20260319.tar acceptance-web:20260319
```

## 3. 上传到服务器

上传这 5 个文件到：

- `/home/ubuntu/apps/acceptance-spec-system/image-deploy`

需要上传的文件：

- `.deploy/docker-compose.images.yml`
- `.deploy/production.env.example`（仅首次部署参考，不覆盖现有 `.env`）
- `deploy/validate-production-env.sh`
- `acceptance-api-20260319.tar`
- `acceptance-web-20260319.tar`

说明：

- 服务器实际生效文件名是 `docker-compose.yml`
- 如果服务器里已经有 `.env`，后续不要用示例文件覆盖
- 首次部署和已有环境升级都必须在 `.env` 中设置 `AUTH_SEED_ADMIN_PASSWORD`、`AUTH_SEED_COMMON_PASSWORD`；两项均至少 12 位，且不得沿用示例值
- `JWT_SIGNING_KEY` 至少 32 位，并应使用独立生成的随机值

已有环境升级时，必须运行机器校验：

```bash
sh validate-production-env.sh .env
```

校验只输出缺失、不合规或仍为已知占位符的变量名，绝不会回显配置值。缺少任一项时，请先写入真实密钥再启动新版本。`production.env.example` 的敏感值故意留空，不可直接部署；Production 环境也不会为初始化账号回退到开发默认口令。

`APP_NETWORK_SUBNET` 必须设置为与宿主机、VPN 和其他 Docker 网络不重叠的 RFC1918 小网段。该值既创建 Compose 网络，也限定 API 信任的紧邻 Nginx 代理范围；不得配置为全网或其他宽泛网段。

若目标数据库存在待执行的破坏性迁移，必须在停止全部 API 副本、完成备份与恢复验证后，按 [Docker 部署指南](../docs/DEPLOY-DOCKER.md) 运行预检和单个 `api --apply-destructive-migrations --backup-verified` 容器。常规 API 启动不会在已有数据库上自动执行这些迁移。

## 4. 服务器更新发布

登录服务器后执行：

```bash
cd /home/ubuntu/apps/acceptance-spec-system/image-deploy
mv docker-compose.images.yml docker-compose.yml
sudo docker load -i acceptance-api-20260319.tar
sudo docker load -i acceptance-web-20260319.tar
sh validate-production-env.sh .env
sed -i 's#^API_IMAGE=.*#API_IMAGE=acceptance-api:20260319#' .env
sed -i 's#^WEB_IMAGE=.*#WEB_IMAGE=acceptance-web:20260319#' .env
sudo docker compose --env-file .env -f docker-compose.yml up -d
sudo docker compose --env-file .env -f docker-compose.yml ps
```

Compose 会把数据库备份写入持久化卷 `api-backups`（容器内目录 `/app/backups`），容器重建不会清空该卷。生产环境仍应定期把备份复制到异机或对象存储，并验证恢复流程；Docker 卷不能替代离机备份。

## 5. 验证

```bash
curl http://127.0.0.1:15290/health/ready
curl -I http://127.0.0.1:18080
```

本项目当前按无 SSO 的内网同站 HTTP 模式部署。浏览器应始终通过一个固定的内网主机名或 IP 访问 `18080` 的 Web 入口，由 Web 入口同站代理 API；不要让用户直接访问 `15290`。示例：

- `http://acceptance.internal:18080`

`.env` 必须显式开启受控内网 HTTP 模式，使用非 `__Host-` 的 host-only Cookie 名、`SameSite=Strict`、`CookieSecure=false`，并让 `CORS_ALLOWED_ORIGIN` 与上述入口的协议、主机名和端口完全一致。不得配置 Cookie Domain、通配 Origin 或跨站入口。

```env
CORS_ALLOWED_ORIGIN=http://acceptance.internal:18080
BROWSER_AUTH_ALLOW_INSECURE_HTTP=true
BROWSER_AUTH_REFRESH_COOKIE_NAME=acceptance-refresh
BROWSER_AUTH_COOKIE_SECURE=false
BROWSER_AUTH_COOKIE_SAME_SITE=Strict
BROWSER_AUTH_COOKIE_DOMAIN=
```

HTTP 是明文传输。`HttpOnly`、CSRF 和 Origin 校验不能阻止内网监听或中间人读取登录口令、AccessToken 或 Cookie。只应在受信任的隔离网段/VLAN中使用，配合主机防火墙限制来源；不得暴露到互联网或不可信无线网络。若无法保证链路可信，应改用内部 CA 或反向代理提供 HTTPS。

## 6. 回滚

如果新版本有问题，只需要把 `.env` 里的镜像标签改回旧版本，然后重新拉起：

```bash
cd /home/ubuntu/apps/acceptance-spec-system/image-deploy
sed -i 's#^API_IMAGE=.*#API_IMAGE=acceptance-api:旧版本标签#' .env
sed -i 's#^WEB_IMAGE=.*#WEB_IMAGE=acceptance-web:旧版本标签#' .env
sudo docker compose --env-file .env -f docker-compose.yml up -d
```

## 7. 清理

镜像导入成功后，可删除服务器上的 tar 包：

```bash
rm -f acceptance-api-20260319.tar acceptance-web-20260319.tar
```

如果旧镜像确认不再使用，再执行：

```bash
sudo docker image prune -a
```

注意：`prune -a` 会删除所有未被容器使用的镜像，执行前先确认服务器上没有别的镜像要保留。

不要对生产部署执行 `docker compose down -v`。`-v` 会同时删除 `mysql-data`、`api-files`、`api-dpkeys` 和 `api-backups`，包括数据库、本地文件、DataProtection 密钥及本机备份。
