## MODIFIED Requirements
### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
系统 SHALL 以服务端当前匹配结果和决策门禁为准，不再暴露或信任旧的 suggestion / compatibility 字段，并允许客户端为本次导出提交受限的验收/备注覆盖值。

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** 客户端调用智能填充预览接口
- **THEN** 预览配置与结果仅暴露召回、歧义、实体判别、复核与等价裁决相关字段
- **AND** 不再暴露 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows`、`LlmSuggestionScoreThreshold` 或 `LlmSuggestion`

#### Scenario: 执行接口接受本次导出覆盖值
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求允许在单行映射中提交 `overrideAcceptance` 与 `overrideRemark`
- **AND** 服务端仅将它们用于本次执行写回
- **AND** 不将这些覆盖值持久化回验收规格主数据

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求仅允许提交当前文件定位、目标列、匹配范围、匹配配置、用户确认映射和本次导出覆盖值
- **AND** 服务端在执行前按当前文件与配置重算门禁
- **AND** 不要求也不接受 `SourceFileId`、`SourceTableIndex`、`SelectedSpecId`、`Acceptance`、`Remark` 或其他旧兼容透传字段
- **AND** 当请求携带这些旧字段时，接口在请求解析阶段直接返回 `400 Bad Request`，而不是静默忽略
