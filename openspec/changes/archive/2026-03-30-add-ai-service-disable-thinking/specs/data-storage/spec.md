## ADDED Requirements
### Requirement: AI服务配置持久化思考模式开关
系统 SHALL 持久化存储 AI 服务的关闭思考模式配置。

#### Scenario: 保存关闭思考模式
- **WHEN** 管理员为某个 LLM AI 服务开启关闭思考模式
- **THEN** 系统将该开关随 AI 服务配置一起保存到数据库
