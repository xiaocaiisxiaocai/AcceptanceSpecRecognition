## ADDED Requirements

### Requirement: 验收规格清理工作流进入应用层

系统 MUST 将扫描任务、分类判定、批量隔离、恢复和永久删除编排放入独立 Application 用例服务，并让协议层只承担请求适配。

#### Scenario: 控制器只做协议适配
- **WHEN** 客户端调用扫描或隔离生命周期 API
- **THEN** 控制器只接收参数、解析访问上下文并包装响应
- **AND** 扫描、并发校验、状态转换和事务由 Application 服务负责

#### Scenario: 扫描判定可独立测试
- **WHEN** 系统根据引用与内容活动数据分类规格
- **THEN** 分类规则实现为不依赖 HTTP 或数据库 Provider 的确定性策略
- **AND** 时间边界通过注入的时间来源测试

### Requirement: Active 规格过滤采用共享边界

系统 MUST 为普通验收规格消费者提供共享的 Active 状态过滤规则，避免隔离语义散落在各用例中。

#### Scenario: 新增规格消费者
- **WHEN** 开发者新增使用验收规格的查询或后台任务
- **THEN** 该消费者复用共享 Active 过滤边界
- **AND** 不需要自行复制隔离状态判断

#### Scenario: 隔离管理显式越过普通过滤
- **WHEN** 清理应用服务查询隔离区
- **THEN** 该用例通过显式的管理查询读取 Quarantined 规格
- **AND** 普通业务仓储接口仍不得返回隔离规格
