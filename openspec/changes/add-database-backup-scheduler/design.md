## Context
系统已存在多个后台维护服务，并且生产部署通过 Docker Compose 运行 `web + api + mysql`。数据库备份需要保存在宿主机可挂载目录中，不能写入数据库本身。

## Decisions
- 使用单行数据库表保存页面配置和最近一次备份状态，`appsettings` 仅提供默认值。
- 后端定时服务按每日本地时间执行；页面保存后下一轮读取立即生效。
- 备份执行通过 `mysqldump` 生成 SQL，再用 gzip 压缩为 `.sql.gz`。
- 第一版只提供备份和手动立即备份，不提供页面恢复，避免误覆盖生产数据。
- 备份目录默认 `/app/backups`，Docker Compose 挂载为独立 volume。

## Risks
- API 容器内必须存在 `mysqldump`/`gzip`。
- 数据库账号必须具备导出所需权限。
- 备份目录必须挂载到宿主机或外部 volume，否则容器重建会丢文件。
