## ADDED Requirements

### Requirement: 验收规格 API 返回引用次数

系统 SHALL 在验收规格读模型中返回当前内容版本的引用次数，并将该字段保持为只读派生状态。

#### Scenario: 列表返回引用次数
- **WHEN** 客户端查询验收规格分页列表
- **THEN** 每条规格结果包含非负的 `referenceCount`

#### Scenario: 详情返回引用次数
- **WHEN** 客户端查询单条验收规格详情
- **THEN** 响应包含与数据库当前值一致的 `referenceCount`

#### Scenario: 客户端不能直接修改引用次数
- **WHEN** 客户端创建或更新验收规格
- **THEN** 请求契约不接受引用次数写入
- **AND** 响应返回由系统维护的当前引用次数
