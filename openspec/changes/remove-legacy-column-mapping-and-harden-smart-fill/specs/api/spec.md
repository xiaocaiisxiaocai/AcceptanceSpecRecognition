## MODIFIED Requirements
### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
系统 SHALL 以服务端当前匹配结果和决策门禁为准，不再暴露或信任旧的 suggestion / compatibility 字段；同时执行权限与下载权限分离，执行成功后允许基于任务标识独立重试下载。

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** 客户端调用智能填充预览接口
- **THEN** 预览配置与结果仅暴露召回、歧义、实体判别、复核与等价裁决相关字段
- **AND** 不再暴露 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows`、`LlmSuggestionScoreThreshold` 或 `LlmSuggestion`

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求仅允许提交当前文件定位、目标列、匹配范围、匹配配置和用户确认映射
- **AND** 服务端在执行前按当前文件与配置重算门禁
- **AND** 不要求也不接受 `SourceFileId`、`SourceTableIndex`、`SelectedSpecId`、`Acceptance`、`Remark` 或其他旧兼容透传字段
- **AND** 当请求携带这些旧字段时，接口在请求解析阶段直接返回 `400 Bad Request`，而不是静默忽略

#### Scenario: 执行成功后允许独立重试下载
- **GIVEN** 智能填充执行已经成功并返回任务标识
- **WHEN** 客户端后续单独调用下载接口
- **THEN** 下载接口仍可仅基于任务标识返回结果文件
- **AND** 下载权限不足不应阻止此前的执行接口完成

## REMOVED Requirements
### Requirement: 智能填充严格复用预检与执行 API
**Reason**: 当前分支已移除一次性严格复用能力，主链只保留当前文件的 AI 智能填充与下载结果。
**Migration**: 前端与调用方不再请求 `/api/matching/reuse/strict/*`。

### Requirement: 智能填充严格复用只允许绑定当前填充结果
**Reason**: 严格复用整体能力已经下线，不再需要会话约束要求。
**Migration**: 无。

### Requirement: 列映射规则管理 API
**Reason**: 旧列映射规则能力已删除，data-import 不再依赖服务端规则自动预填。
**Migration**: 前端移除 `/api/column-mapping-rules` 相关调用；现有数据库通过迁移删除旧表。

#### Scenario: 旧列映射规则接口不可再访问
- **WHEN** 客户端调用 `/api/column-mapping-rules` 及其子路由
- **THEN** 系统不再提供这些接口
