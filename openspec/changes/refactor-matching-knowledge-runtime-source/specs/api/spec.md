## MODIFIED Requirements
### Requirement: 运行时匹配知识不提供对外配置 API
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，不再提供 `/api/matching-knowledge` 及其派生配置接口。

#### Scenario: 客户端访问旧匹配知识读写接口
- **WHEN** 客户端访问 `GET /api/matching-knowledge`、`PUT /api/matching-knowledge`、`POST /api/matching-knowledge/clear` 或 `POST /api/matching-knowledge/restore-defaults`
- **THEN** 系统不再提供这些接口
- **AND** 运行时不再以数据库中的 matching-knowledge 配置作为来源

#### Scenario: 客户端访问旧草稿接口
- **WHEN** 客户端访问 `POST /api/matching-knowledge/drafts/generate`
- **THEN** 系统不再提供该接口
- **AND** 不再暴露 `builtIn`、`custom`、`effective` 等旧分层视图语义
