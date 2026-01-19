## MODIFIED Requirements

### Requirement: 客户制程选择
系统 SHALL 允许用户**分别选择客户与制程**，并以该组合定义一整份验规（验规集合）的范围。

#### Scenario: 独立选择
- **WHEN** 用户在导入页/验收规格页/智能填充页选择范围
- **THEN** UI 分别提供客户选择器与制程选择器（不要求制程必须隶属某客户）

#### Scenario: 组合筛选
- **WHEN** 用户选择客户与制程后
- **THEN** UI 以 (CustomerId, ProcessId) 组合查询并展示对应验规集合内的数据

