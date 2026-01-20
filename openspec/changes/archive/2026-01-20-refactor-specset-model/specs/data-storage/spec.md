## MODIFIED Requirements

### Requirement: 数据组织
系统 MUST 将验收规格按“客户 + 制程”的**组合维度**组织为一整份验规（验规集合）。

#### Scenario: 验规集合的定义
- **WHEN** 用户在系统中选择一个客户与一个制程
- **THEN** 系统以该 (CustomerId, ProcessId) 组合为边界展示/查询/导入该组合下的所有验收规格条目

#### Scenario: 客户与制程独立
- **WHEN** 系统维护基础数据时
- **THEN** Customer 与 Process 互不从属（Process 不包含 CustomerId 外键）

### Requirement: 数据库约束与索引
系统 SHALL 为验收规格建立用于组合筛选的索引以保证查询性能。

#### Scenario: 组合筛选索引
- **WHEN** 系统按 (CustomerId, ProcessId) 查询验收规格列表
- **THEN** 查询性能保持可接受（存在 `AcceptanceSpecs(CustomerId, ProcessId)` 索引或等价实现）

