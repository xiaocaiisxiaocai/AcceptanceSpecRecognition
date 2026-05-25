## ADDED Requirements

### Requirement: 数据库备份配置持久化
系统 SHALL 在数据库中持久化数据库备份页面配置和最近一次备份状态。

#### Scenario: 服务重启后保持备份配置
- **GIVEN** 管理员已保存数据库备份配置
- **WHEN** API 服务重启
- **THEN** 系统仍按数据库中保存的配置执行备份计划

#### Scenario: 保留备份文件数量
- **GIVEN** 管理员配置了备份保留份数
- **WHEN** 新备份执行完成
- **THEN** 系统删除超过保留份数的旧备份文件
