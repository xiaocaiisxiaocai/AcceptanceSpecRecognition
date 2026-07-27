## ADDED Requirements

### Requirement: Embedding 缓存并发写入幂等
系统 MUST 以 `(SpecId, ModelName, Usage)` 唯一约束为最终裁决，并让同键并发生成安全复用胜出记录。

#### Scenario: 两个请求并发创建同一缓存
- **GIVEN** 两个请求同时确认目标缓存不存在
- **WHEN** 两个请求尝试插入相同 `(SpecId, ModelName, Usage)` 的记录
- **THEN** 数据库最终只保留一条记录
- **AND** 唯一键竞争失败的请求重新读取并返回胜出记录
- **AND** 客户端不因该目标唯一键竞争收到通用 `500`

#### Scenario: 非目标数据库错误
- **WHEN** 缓存保存发生非目标唯一键、外键或其他数据库错误
- **THEN** 系统不得把该错误当作并发复用成功
- **AND** 按统一错误边界记录并返回

### Requirement: 持久文件删除状态可恢复
系统 MUST 持久化文件待删除状态、重试信息和最后失败原因，使数据库与文件系统之间的最终一致过程可观察、可重试。

#### Scenario: 请求删除持久文件
- **WHEN** 用户通过真实持久文件删除入口确认删除
- **THEN** 数据库事务先把记录标记为 `PendingDeletion`
- **AND** 记录删除请求时间和待处理文件标识
- **AND** 数据库迁移本身不删除物理文件

#### Scenario: 物理删除失败
- **GIVEN** 记录处于 `PendingDeletion`
- **WHEN** 清理器无法删除物理文件
- **THEN** 数据库保留该记录
- **AND** 更新失败原因、重试次数和下次重试时间

#### Scenario: 物理文件已经不存在
- **GIVEN** 记录处于 `PendingDeletion`
- **AND** 目标物理文件已经不存在
- **WHEN** 清理器处理该记录
- **THEN** 系统把物理删除视为幂等成功
- **AND** 完成元数据清理或归档
