## 1. Backend
- [x] 1.1 新增数据库备份配置实体、DbSet 与 EF 迁移。
- [x] 1.2 新增备份配置 DTO、Manager、Executor 与 HostedService。
- [x] 1.3 新增数据库备份配置 API，支持读取、保存、立即备份。
- [x] 1.4 补充后端测试，覆盖配置持久化、手动备份、执行状态与保留份数。

## 2. Frontend
- [x] 2.1 新增数据库备份 API 封装。
- [x] 2.2 新增配置页面和路由入口。
- [x] 2.3 接入导航权限清单与按钮权限。

## 3. Deployment
- [x] 3.1 API Docker 镜像安装 MySQL 客户端工具。
- [x] 3.2 Docker Compose 增加备份目录挂载与默认环境配置。

## 4. Verification
- [x] 4.1 运行后端相关测试与解决方案测试。
- [x] 4.2 运行前端构建。
- [x] 4.3 验证工作区状态并提交推送。
