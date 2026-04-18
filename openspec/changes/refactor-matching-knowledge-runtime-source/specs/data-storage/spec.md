## MODIFIED Requirements
### Requirement: 运行时匹配知识不依赖外部持久化配置
系统 SHALL 将匹配知识视为匹配引擎内部运行时能力，不再要求存在 matching-knowledge 单例配置表或其对外持久化契约。

#### Scenario: 系统启动
- **WHEN** 系统启动并初始化匹配引擎
- **THEN** 系统不再要求从 matching-knowledge 单例配置表读取对外可维护配置
- **AND** 不再承诺“清空后保持空结果”“恢复默认配置”等旧持久化语义
