# Change: 新增数据库定时备份配置

## Why
当前 Docker 部署依赖 MySQL 持久化卷，但系统内没有可视化的数据库备份配置和执行入口。管理员需要在页面上配置备份计划，并能手动触发备份，降低远程部署维护成本。

## What Changes
- 新增数据库备份配置页面，支持启用、每日执行时间、备份目录、保留份数配置。
- 新增后端数据库备份 API，支持读取配置、保存配置、手动执行备份。
- 新增后台定时服务，按配置调用 `mysqldump` 生成 `.sql.gz` 备份文件并清理旧备份。
- 新增数据库持久化表保存备份配置和最近一次执行状态。
- Docker API 镜像安装 MySQL 客户端，并挂载备份目录卷。

## Impact
- Affected specs: `api`, `data-storage`, `user-interface`
- Affected code: API 控制器/服务、EF 实体与迁移、前端配置页、导航权限、Dockerfile、docker-compose
