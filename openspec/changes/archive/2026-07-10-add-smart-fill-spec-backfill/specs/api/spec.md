## MODIFIED Requirements
### Requirement: 智能填充预览与执行接口不再暴露旧兼容字段
系统 SHALL 以服务端当前匹配结果和决策门禁为准，不再暴露或信任旧的 suggestion / compatibility 字段，并允许客户端为本次导出提交受限的验收/备注覆盖值和未命中行手工填充值；系统 SHALL 提供独立接口，允许客户端将用户手动修改过的智能填充预览行选择性回填到验收规格主数据。

#### Scenario: 预览契约不再包含旧 suggestion 语义
- **WHEN** 客户端调用智能填充预览接口
- **THEN** 预览配置与结果仅暴露召回、歧义、实体判别、复核与等价裁决相关字段
- **AND** 不再暴露 `UseLlmReview`、`UseLlmSuggestion`、`SuggestNoMatchRows`、`LlmSuggestionScoreThreshold` 或 `LlmSuggestion`

#### Scenario: 执行接口接受本次导出覆盖值
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求允许在单行映射中提交 `overrideAcceptance` 与 `overrideRemark`
- **AND** 服务端仅将它们用于本次执行写回
- **AND** 除非客户端另行调用回填验收规格接口，否则不将这些覆盖值持久化回验收规格主数据

#### Scenario: 仅精确匹配预览不进入语义与 AI 裁决
- **WHEN** 客户端提交预览请求且 `exactMatchOnly` 为 `true`
- **THEN** 服务端仍校验请求中的 Embedding 服务配置可用
- **AND** 仅按源行与候选验收规格的 `项目+规格` 完全一致关系生成匹配结果
- **AND** 对完全一致的行返回可自动采用的精确命中结果
- **AND** 对不完全一致的行返回未命中预览结果
- **AND** 不执行语义 TopK 召回、Embedding 相似度匹配或 AI 等价裁决

#### Scenario: 执行接口接受未命中行手工填充
- **WHEN** 客户端提交智能填充执行请求，某行映射没有 `specId` 但 `manualFill` 为 `true`
- **THEN** 请求允许该行携带 `overrideAcceptance` 与 `overrideRemark`
- **AND** 服务端将这些值写入本次结果文件
- **AND** 不要求该行存在验收规格主数据
- **AND** 除非客户端另行调用回填验收规格接口，否则不将这些手工填写值持久化回验收规格主数据

#### Scenario: 回填接口更新已匹配规格
- **WHEN** 客户端提交回填验收规格请求，某项包含有效 `specId`
- **THEN** 服务端校验该验收规格在当前用户可访问的数据范围内
- **AND** 服务端仅更新该规格的 `Acceptance` 与 `Remark`
- **AND** 服务端不更新该规格的 `Project` 与 `Specification`
- **AND** 服务端使该规格相关 Embedding 缓存失效或重新生成

#### Scenario: 回填接口新增未匹配规格
- **WHEN** 客户端提交回填验收规格请求，某项不包含 `specId`
- **THEN** 请求必须包含源行 `Project`、`Specification`、编辑后的 `Acceptance` 或 `Remark`
- **AND** 请求必须包含当前匹配范围内的 `customerId`，并可包含 `processId` 与 `machineModelId`
- **AND** 服务端校验当前用户可写入该数据范围
- **AND** 服务端新增验收规格并返回新增数量

#### Scenario: 回填接口拒绝无效项
- **WHEN** 客户端提交空回填列表、无编辑内容、越权 `specId` 或缺少必要范围信息的请求
- **THEN** 服务端返回明确失败信息
- **AND** 不写入任何验收规格主数据

#### Scenario: 执行接口不再信任旧客户端决策字段
- **WHEN** 客户端提交智能填充执行请求
- **THEN** 请求仅允许提交当前文件定位、目标列、匹配范围、匹配配置、用户确认映射、本次导出覆盖值和手工填充标记
- **AND** 服务端在执行前按当前文件与配置重算门禁
- **AND** 不要求也不接受 `SourceFileId`、`SourceTableIndex`、`SelectedSpecId`、`Acceptance`、`Remark` 或其他旧兼容透传字段
- **AND** 当请求携带这些旧字段时，接口在请求解析阶段直接返回 `400 Bad Request`，而不是静默忽略
