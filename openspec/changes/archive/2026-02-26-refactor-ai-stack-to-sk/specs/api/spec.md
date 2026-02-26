## ADDED Requirements

### Requirement: AI 服务配置 API
系统 SHALL 提供 AI 服务配置的 REST API，用于创建、查询、更新与删除服务配置。

#### Scenario: 服务列表查询
- **WHEN** 前端发送 GET 请求到 /api/ai-services
- **THEN** 系统返回 AI 服务配置列表

#### Scenario: 新增服务配置
- **WHEN** 前端发送 POST 请求到 /api/ai-services
- **THEN** 系统创建 AI 服务配置并返回结果

#### Scenario: 更新服务配置
- **WHEN** 前端发送 PUT 请求到 /api/ai-services/{id}
- **THEN** 系统更新指定 AI 服务配置

#### Scenario: 删除服务配置
- **WHEN** 前端发送 DELETE 请求到 /api/ai-services/{id}
- **THEN** 系统删除指定 AI 服务配置

---

### Requirement: AI 服务连接测试与模型探测 API
系统 SHALL 通过 Semantic Kernel 连接器提供连接测试与模型探测接口。

#### Scenario: 连接测试
- **WHEN** 前端发送 POST 请求到 /api/ai-services/{id}/test
- **THEN** 系统测试连接并返回成功或失败原因

#### Scenario: 模型列表探测
- **WHEN** 前端发送 GET 请求到 /api/ai-services/{id}/models
- **THEN** 系统返回可用模型列表或失败原因
