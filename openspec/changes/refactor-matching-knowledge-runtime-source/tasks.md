## 1. 后端语义收敛
- [x] 1.1 移除匹配知识运行时 `builtIn + custom + effective` 合并语义，改为数据库单一生效配置语义
- [x] 1.2 调整 `ConfigurationMatchingKnowledgeProvider`，运行时仅从数据库读取当前配置
- [x] 1.3 调整 `MatchingKnowledgeBootstrapper`，数据库为空时导入默认种子，而不是写入空配置
- [x] 1.4 重构 `MatchingKnowledgeComposition`，仅保留标准化、序列化与种子导入辅助逻辑

## 2. API 与 DTO 调整
- [x] 2.1 调整 `GET /api/matching-knowledge` 返回单层当前配置，不再返回 `builtIn/custom/effective`
- [x] 2.2 调整 `PUT /api/matching-knowledge` 为保存当前完整配置，而不是仅保存自定义扩展
- [x] 2.3 新增显式“清空当前配置”与“恢复默认种子”接口
- [x] 2.4 调整匹配知识相关 DTO 与接口校验逻辑

## 3. 前端页面调整
- [x] 3.1 将匹配知识配置页改为单一可编辑视图，不再展示内置层与自定义层分栏
- [x] 3.2 将“重置默认”拆分为“清空当前配置”和“恢复默认”两个明确操作
- [x] 3.3 调整 AI 草稿导入逻辑，基于数据库当前配置去重与合并
- [x] 3.4 更新前端 API 封装与页面提示文案

## 4. 验证与兼容
- [x] 4.1 补充 API 测试，验证清空、恢复默认、保存完整配置和重启后持久化行为
- [x] 4.2 补充前端交互验证，确认页面展示与运行时配置一致
- [x] 4.3 验证删除默认知识项后不会在服务重启或再次读取时被隐式回补
- [x] 4.4 运行 `openspec validate refactor-matching-knowledge-runtime-source --strict`
