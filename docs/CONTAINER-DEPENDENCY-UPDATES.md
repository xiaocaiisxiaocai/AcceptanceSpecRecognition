# 容器基础镜像与非 root 运行维护

## 当前约束

- API build/runtime、Web build/runtime 和 MySQL 镜像均使用“明确版本标签 + `sha256` digest”。
- API runtime 使用 .NET 官方 `app` 用户；Web runtime 使用 `nginxinc/nginx-unprivileged` 的 `nginx` 用户。
- API 与 Web 根文件系统为只读，只允许 `/tmp`、Nginx 运行目录和显式业务卷写入。
- digest 更新必须作为显式依赖更新提交，不能只修改标签或依赖远端 mutable tag 漂移。

## 更新流程

1. 查询候选镜像的多架构 manifest digest：

   ```powershell
   docker buildx imagetools inspect <image:version> --format '{{json .Manifest.Digest}}'
   ```

2. 同时更新 Dockerfile/Compose 中的版本标签与 digest，并记录上游安全公告或发行说明。
3. 构建 API/Web 镜像，验证镜像配置用户不是 `root`/`0`。
4. 以只读根文件系统和临时目录运行 smoke test，验证健康检查、静态页面及业务卷可写。
5. 用 `.deploy/docker-compose.images.yml` 执行 `docker compose config`，再演练使用上一组不可变镜像标签回滚；不得删除数据卷。

## 已有卷升级

旧版本可能以 root 身份创建 `api-files`、`api-dpkeys` 或 `api-backups`。上线非 root 镜像前，运维人员必须停写并使用一次性受控维护容器把这三个卷的目录所有者调整为 .NET `APP_UID`，随后立即移除维护容器。不得为了兼容旧卷把生产 API 改回 root；应先在副本或预发布环境验证权限迁移和回滚。

2026-07-11 的镜像、非 root 卷写入、真实 MySQL、浏览器 E2E 与不可变镜像回滚证据见 `PRODUCTION-EQUIVALENT-VERIFICATION-2026-07-11.md`。
