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

上传这 4 个文件到：

- `/home/ubuntu/apps/acceptance-spec-system/image-deploy`

需要上传的文件：

- `.deploy/docker-compose.images.yml`
- `.deploy/production.env.example`（仅首次部署参考，不覆盖现有 `.env`）
- `acceptance-api-20260319.tar`
- `acceptance-web-20260319.tar`

说明：

- 服务器实际生效文件名是 `docker-compose.yml`
- 如果服务器里已经有 `.env`，后续不要用示例文件覆盖

## 4. 服务器更新发布

登录服务器后执行：

```bash
cd /home/ubuntu/apps/acceptance-spec-system/image-deploy
mv docker-compose.images.yml docker-compose.yml
sudo docker load -i acceptance-api-20260319.tar
sudo docker load -i acceptance-web-20260319.tar
sed -i 's#^API_IMAGE=.*#API_IMAGE=acceptance-api:20260319#' .env
sed -i 's#^WEB_IMAGE=.*#WEB_IMAGE=acceptance-web:20260319#' .env
sudo docker compose --env-file .env -f docker-compose.yml up -d
sudo docker compose --env-file .env -f docker-compose.yml ps
```

## 5. 验证

```bash
curl http://127.0.0.1:15290/health
curl -I http://127.0.0.1:18080
```

外网地址：

- `http://134.175.195.207:18080`
- `http://134.175.195.207:15290/health`

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
