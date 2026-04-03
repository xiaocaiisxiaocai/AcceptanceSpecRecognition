## ADDED Requirements
### Requirement: 匹配知识配置数据库持久化
系统 SHALL 在 MySQL 中持久化当前生效的单例匹配知识配置。

#### Scenario: 服务重启后仍保持相同配置
- **GIVEN** 用户已保存一套匹配知识配置
- **WHEN** 服务重启后再次读取匹配知识
- **THEN** 系统返回与重启前一致的配置内容

#### Scenario: 始终只有一套生效配置
- **WHEN** 系统读取当前匹配知识配置
- **THEN** 系统返回唯一的当前生效配置
- **AND** 不要求用户在多个版本之间手工切换

### Requirement: 旧文本预处理数据迁移与清理
系统 SHALL 在引入数据库化匹配知识配置时，保守迁移可识别旧同义词并直接清理旧表。

#### Scenario: 迁移可识别的旧同义词
- **GIVEN** 旧同义词数据中存在能够明确归类为实体别名、单位别名或字段别名的词组
- **WHEN** 系统执行迁移
- **THEN** 系统将这些可识别词组迁移到对应的匹配知识槽位

#### Scenario: 丢弃不可识别旧数据
- **GIVEN** 旧同义词、关键字或文本预处理配置中存在无法明确映射到结构化知识槽位的数据
- **WHEN** 系统执行迁移
- **THEN** 系统直接丢弃这些旧数据
- **AND** 不将其写入新的匹配知识配置

#### Scenario: 删除旧配置表
- **WHEN** 数据迁移完成
- **THEN** 系统删除 `SynonymGroups`、`SynonymWords`、`Keywords` 和 `TextProcessingConfigs` 旧表

