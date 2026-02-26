## MODIFIED Requirements

### Requirement: 验收规格按客户制程组合组织
系统 MUST 将验收规格按“客户 + 制程(可选) + 机型(可选)”的组合维度组织。

#### Scenario: 客户范围查询
- **WHEN** 用户仅选择客户
- **THEN** 系统按CustomerId筛选验收规格

#### Scenario: 客户+制程查询
- **WHEN** 用户选择客户与制程
- **THEN** 系统按(CustomerId, ProcessId)组合筛选验收规格

#### Scenario: 客户+机型查询
- **WHEN** 用户选择客户与机型
- **THEN** 系统按(CustomerId, MachineModelId)组合筛选验收规格

#### Scenario: 客户+制程+机型查询
- **WHEN** 用户同时选择客户、制程与机型
- **THEN** 系统按(CustomerId, ProcessId, MachineModelId)组合筛选验收规格

---

## ADDED Requirements

### Requirement: 机型基础数据
系统 SHALL 维护全局独立的机型基础数据，并可与验收规格关联。

#### Scenario: 机型表结构
- **WHEN** 系统初始化数据结构
- **THEN** 数据库存在 MachineModels 表，包含名称与创建时间

#### Scenario: 验收规格可选机型
- **WHEN** 验收规格创建/更新
- **THEN** MachineModelId 可为空，且不影响客户必填约束
