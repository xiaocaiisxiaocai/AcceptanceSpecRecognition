## ADDED Requirements

### Requirement: 运行时实体候选提取
系统 SHALL 在匹配运行时对源项与候选项文本执行实体候选提取和轻量归一化，即使未命中匹配知识配置，也应尽量提取品牌或组织实体候选。

#### Scenario: 无配置场景提取英文品牌
- **GIVEN** 源文本包含 `Panasonic 设备`
- **AND** 当前匹配知识配置中未包含 `Panasonic -> 松下`
- **WHEN** 系统执行多阶段匹配
- **THEN** 系统仍然提取出源项实体候选 `Panasonic`
- **AND** 该候选可用于后续实体关系判别

### Requirement: LLM 实体关系判别
系统 SHALL 在运行时可选地使用 LLM 对实体候选关系进行判别，但该判别仅用于实体证据，不得直接替代整体匹配排序。

#### Scenario: 中英文品牌被判为别名同一
- **GIVEN** 源项实体候选为 `Panasonic`
- **AND** 候选项实体候选为 `松下`
- **AND** 用户已开启 LLM 实体判别
- **WHEN** 系统对 Top 候选执行实体关系判别
- **THEN** 系统输出 `alias_same` 或 `same`
- **AND** 将其记为正向实体证据

#### Scenario: 品牌冲突被判为不同实体
- **GIVEN** 源项实体候选为 `Panasonic`
- **AND** 候选项实体候选为 `Mitsubishi`
- **AND** 用户已开启 LLM 实体判别
- **WHEN** 系统执行实体关系判别
- **THEN** 系统输出 `conflict`
- **AND** 系统为该候选生成实体冲突问题说明

### Requirement: 未知实体保守降级
系统 SHALL 对无法确认关系的实体候选输出 `unknown`，并保守降级为人工确认，而不是直接拒绝或自动采用。

#### Scenario: 未知品牌无法确认关系
- **GIVEN** 源项实体候选为 `XJTech`
- **AND** 候选项实体候选为 `新境科技`
- **AND** LLM 无法确认两者是否为同一实体
- **WHEN** 系统完成实体关系判别
- **THEN** 系统输出 `unknown`
- **AND** 结果至少降级为 `manualReview`
- **AND** 系统输出 `entity_unknown` 问题说明

### Requirement: 硬规则优先于实体判别
系统 SHALL 保持数值、型号、冲突词等硬规则优先级，不允许实体同一证据推翻已存在的硬冲突。

#### Scenario: 数值硬冲突不得被实体同一覆盖
- **GIVEN** 源项与候选项都指向同一品牌
- **AND** 源项包含 `电压等于24V`
- **AND** 候选项包含 `电压等于2.4V`
- **WHEN** 系统完成实体判别和多阶段重排
- **THEN** 系统仍将该候选视为数值硬冲突
- **AND** 最终结果不得因为实体同一而自动采用
