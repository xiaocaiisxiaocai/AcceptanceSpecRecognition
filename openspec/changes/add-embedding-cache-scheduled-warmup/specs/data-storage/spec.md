## ADDED Requirements
### Requirement: Embedding 缓存按用途与文本指纹持久化
系统 SHALL 在数据库中保存 Embedding 缓存的业务用途与文本指纹，避免不同文本边界的向量被错误复用。

#### Scenario: 匹配与语义搜索缓存隔离
- **GIVEN** 同一条验收规格参与智能匹配和 AI 语义搜索
- **WHEN** 系统保存 Embedding 缓存
- **THEN** 智能匹配缓存与语义搜索缓存使用不同用途标识
- **AND** 缓存命中必须匹配规格ID、模型名称、用途和文本指纹

#### Scenario: 文本变化导致缓存失效
- **GIVEN** 某条验收规格的项目、规格、验收或备注发生变化
- **WHEN** 系统后续读取 Embedding 缓存
- **THEN** 旧文本指纹对应缓存不得被当作有效缓存命中
- **AND** 系统可以重新生成当前文本对应的缓存

### Requirement: Embedding 缓存支持定时补齐
系统 SHALL 支持后台任务扫描缺失或过期的 Embedding 缓存，并以批处理方式写入数据库。

#### Scenario: 历史数据缺失缓存
- **GIVEN** 数据库中存在未生成 Embedding 缓存的历史验收规格
- **WHEN** 定时预热任务运行
- **THEN** 系统为缺失缓存的规格批量生成向量
- **AND** 将向量保存到 `EmbeddingCaches`
