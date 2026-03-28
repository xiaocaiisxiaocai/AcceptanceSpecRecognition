## 1. 数据与迁移
- [x] 1.1 新增 `MatchingKnowledgeConfig` 持久化模型、仓储和 EF Core 迁移
- [x] 1.2 在迁移或初始化过程中写入系统默认匹配知识
- [x] 1.3 将旧同义词中可明确识别的数据迁移到 `EntityAliases`、`UnitAliases`、`FieldAliases`
- [x] 1.4 删除 `SynonymGroups`、`SynonymWords`、`Keywords`、`TextProcessingConfigs` 旧表

## 2. 后端 API 与运行时
- [x] 2.1 新增 `GET /api/matching-knowledge`、`PUT /api/matching-knowledge`、`POST /api/matching-knowledge/reset`
- [x] 2.2 将 `IMatchingKnowledgeProvider` 切换为数据库读取实现，并保留默认值初始化/重置能力
- [x] 2.3 移除文本预处理、同义词、关键字相关 Controller、DTO、服务注册和运行时主链路依赖
- [x] 2.4 为匹配知识保存增加服务端校验与规范化逻辑

## 3. 前端与权限
- [x] 3.1 新增“匹配知识配置”页面和前端 API 封装
- [x] 3.2 用新页面替换“文本处理配置”入口
- [x] 3.3 删除文本预处理、同义词、关键字相关页面、路由和菜单入口
- [x] 3.4 更新内置权限种子，新增 `page:config:matching-knowledge`、`btn:matching-knowledge:update`、`btn:matching-knowledge:reset`

## 4. 验证
- [x] 4.1 增加迁移、provider、API、前端权限和页面回归测试
- [x] 4.2 验证匹配主链路在移除旧文本预处理后仍满足核心行为要求
- [x] 4.3 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`
- [x] 4.4 运行 `pnpm build`
