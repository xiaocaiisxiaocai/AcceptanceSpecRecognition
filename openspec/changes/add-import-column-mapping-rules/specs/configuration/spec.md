## ADDED Requirements

### Requirement: 列映射规则配置持久化
系统 SHALL 将“列映射规则”持久化保存，并支持读取当前生效规则供导入流程使用。

#### Scenario: 保存规则后可在导入中生效
- **WHEN** 用户保存列映射规则
- **THEN** 后续在导入数据流程中，系统能够读取该规则并用于自动预填

### Requirement: 规则匹配模式
系统 SHALL 支持基于表头文本的规则匹配模式：contains、equals、regex，并支持优先级用于冲突决策。

#### Scenario: 多规则冲突时按优先级选中
- **GIVEN** 多条规则同时命中同一表格列
- **WHEN** 系统计算自动映射
- **THEN** 系统选择优先级最高的规则作为映射结果
