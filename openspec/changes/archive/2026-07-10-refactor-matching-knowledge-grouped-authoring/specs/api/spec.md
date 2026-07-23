## MODIFIED Requirements
### Requirement: 运行时匹配知识不提供对外配置 API
系统 SHALL 将匹配知识限制为匹配引擎内部运行时知识，不再提供分组式作者视图、草稿生成或其他 matching-knowledge 配置接口。

#### Scenario: 客户端访问旧分组作者视图接口
- **WHEN** 客户端访问 `GET /api/matching-knowledge` 或 `PUT /api/matching-knowledge`
- **THEN** 系统不再返回实体组、单位组、字段组、左右冲突组等作者视图
- **AND** 不再接受基于分组作者视图的保存请求

#### Scenario: 客户端访问旧草稿与旧配置接口
- **WHEN** 客户端访问 `POST /api/matching-knowledge/drafts/generate`、`/api/text-processing/config`、`/api/synonyms` 或 `/api/keywords`
- **THEN** 系统不再提供这些接口
- **AND** 匹配知识仅保留为运行时内部能力，不再作为现行可维护功能描述
