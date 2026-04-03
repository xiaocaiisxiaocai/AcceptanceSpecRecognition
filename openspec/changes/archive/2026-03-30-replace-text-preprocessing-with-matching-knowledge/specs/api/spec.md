## ADDED Requirements
### Requirement: 统一匹配知识配置 API
系统 SHALL 提供统一的匹配知识配置 API，用于读取、保存和重置当前生效的结构化匹配知识。

#### Scenario: 读取当前匹配知识配置
- **WHEN** 前端发送 `GET /api/matching-knowledge`
- **THEN** 系统返回当前生效的实体别名、单位别名、单位换算、字段别名和冲突词对配置

#### Scenario: 保存匹配知识配置
- **GIVEN** 用户已编辑匹配知识配置
- **WHEN** 前端发送 `PUT /api/matching-knowledge`
- **THEN** 系统校验并持久化整套配置
- **AND** 后续匹配请求读取更新后的配置

#### Scenario: 重置为系统默认配置
- **WHEN** 前端发送 `POST /api/matching-knowledge/reset`
- **THEN** 系统将当前匹配知识恢复为系统默认配置
- **AND** 返回重置后的完整配置

#### Scenario: 旧配置接口移除
- **WHEN** 客户端访问 `/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
- **THEN** 系统不再提供这些旧配置接口

