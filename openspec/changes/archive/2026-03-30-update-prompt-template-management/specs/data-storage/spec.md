## ADDED Requirements
### Requirement: Prompt 模板场景化持久化
系统 SHALL 持久化存储 Prompt 模板的场景、显示名称与系统模板标记。

#### Scenario: 存储系统模板元数据
- **WHEN** 系统保存系统 Prompt 模板
- **THEN** 数据库保存该模板的场景标识、显示名称和系统模板标记

#### Scenario: 旧模板自动映射
- **GIVEN** 数据库中存在仅包含旧名称的 Prompt 模板数据
- **WHEN** 系统完成迁移或首次读取
- **THEN** 系统将旧模板映射到对应系统场景
- **AND** 为缺失系统模板的场景补齐默认内容
