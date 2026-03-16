## MODIFIED Requirements

### Requirement: 候选结果排序与Top-N
系统 SHALL 支持可配置的单阶段与多阶段匹配策略。

#### Scenario: 默认单阶段返回最佳候选
- **GIVEN** 用户未指定匹配策略，或匹配策略为 `SingleStage`
- **WHEN** 系统执行匹配
- **THEN** 系统按 Embedding 得分降序选择最佳候选
- **AND** 返回最高得分的 1 条结果作为默认最佳匹配

#### Scenario: 多阶段模式下先召回再重排
- **GIVEN** 用户将匹配策略设置为 `MultiStage`
- **AND** 用户设置 `RecallTopK=5`
- **WHEN** 系统执行匹配
- **THEN** 系统先按 Embedding 得分召回前 5 条候选
- **AND** 对召回候选执行第二阶段重排
- **AND** 返回重排后的最佳结果作为默认最佳匹配

### Requirement: 阈值过滤
系统 SHALL 支持可配置的匹配阈值；在多阶段模式下，该阈值继续作为第一阶段候选准入阈值。

#### Scenario: 单阶段应用阈值过滤
- **GIVEN** 用户设置匹配阈值为 0.3
- **AND** 系统使用 `SingleStage`
- **AND** 匹配结果中有得分 0.95、0.42、0.28 的记录
- **WHEN** 系统应用阈值过滤
- **THEN** 系统返回得分 0.95 与 0.42 的记录
- **AND** 过滤得分 0.28 的记录

#### Scenario: 多阶段仅召回达到阈值的候选
- **GIVEN** 用户设置匹配阈值为 0.3
- **AND** 系统使用 `MultiStage`
- **AND** 候选的 Embedding 得分分别为 0.61、0.46、0.29、0.22
- **WHEN** 系统执行第一阶段召回
- **THEN** 系统仅允许得分 0.61 与 0.46 的候选进入 `TopK` 召回集合
- **AND** 得分 0.29 与 0.22 的候选不会进入第二阶段重排

#### Scenario: 多阶段无候选达到阈值
- **GIVEN** 用户设置匹配阈值为 0.9
- **AND** 系统使用 `MultiStage`
- **AND** 所有候选的 Embedding 得分均低于 0.9
- **WHEN** 系统执行第一阶段召回
- **THEN** 系统返回无匹配结果
- **AND** 不进入第二阶段重排

### Requirement: 匹配结果包含算法得分明细
系统 SHALL 在匹配结果中返回算法明细；启用多阶段时，还应包含重排相关信息。

#### Scenario: 单阶段返回 Embedding 得分明细
- **WHEN** 系统以单阶段模式返回候选结果
- **THEN** 每条结果包含 `Embedding` 得分明细

#### Scenario: 多阶段返回重排明细
- **GIVEN** 系统启用了多阶段匹配
- **WHEN** 系统返回最佳匹配结果
- **THEN** 结果包含 `Embedding` 得分明细
- **AND** 结果包含重排得分、重排原因或阶段信息摘要

### Requirement: 默认选择最高得分
系统 SHALL 按当前激活策略选择默认最佳匹配。

#### Scenario: 单阶段按 Embedding 最高得分选择
- **GIVEN** 系统使用 `SingleStage`
- **AND** 候选结果得分分别为 0.95、0.88、0.82
- **WHEN** 系统生成预览结果
- **THEN** 得分 0.95 的结果被标记为默认最佳匹配

#### Scenario: 多阶段按重排后结果选择
- **GIVEN** 系统使用 `MultiStage`
- **AND** Embedding 候选中最高分候选不是最终最优业务匹配
- **WHEN** 系统完成规则重排
- **THEN** 重排后的最佳候选被标记为默认最佳匹配

## ADDED Requirements

### Requirement: 多阶段规则重排
系统 SHALL 在多阶段模式下基于结构化信号对召回候选进行第二阶段重排。

#### Scenario: 使用项目与规格信号重排
- **GIVEN** 多个候选具有相近的 Embedding 得分
- **AND** 其中一个候选在项目文本、数值单位或关键词重合方面更符合源数据
- **WHEN** 系统执行规则重排
- **THEN** 系统提高该候选的重排分值
- **AND** 将其优先作为最终结果

### Requirement: 歧义样本按需触发 LLM 复核
系统 SHALL 支持仅对高歧义候选集按需触发 LLM 复核。

#### Scenario: 歧义候选触发 LLM
- **GIVEN** 系统启用了 `RulesPlusLlm`
- **AND** 前两名候选的综合分差小于配置的歧义阈值
- **WHEN** 系统完成规则重排
- **THEN** 系统触发 LLM 复核该样本
- **AND** LLM 仅对该歧义样本参与最终判断

#### Scenario: 非歧义候选跳过 LLM
- **GIVEN** 系统启用了 `RulesPlusLlm`
- **AND** 当前样本的最佳候选明显领先于其它候选
- **WHEN** 系统完成规则重排
- **THEN** 系统不触发 LLM 复核
- **AND** 直接返回当前最佳结果

### Requirement: 歧义判定规则
系统 SHALL 使用统一的歧义判定规则标记高歧义样本。

#### Scenario: 前两名分差小于等于歧义阈值
- **GIVEN** 系统使用 `MultiStage`
- **AND** 重排后的前两名候选最终得分分别为 0.81 与 0.78
- **AND** `AmbiguityMargin = 0.03`
- **WHEN** 系统完成歧义判定
- **THEN** 该样本被标记为高歧义样本

#### Scenario: 仅有一条候选时不标记为歧义
- **GIVEN** 系统使用 `MultiStage`
- **AND** 仅有 1 条候选通过第一阶段阈值
- **WHEN** 系统完成歧义判定
- **THEN** 该样本不被标记为高歧义样本

### Requirement: 预览与执行一致性
系统 SHALL 在执行填充时使用用户在预览阶段确认后的结果，而不是静默替换为新的默认匹配。

#### Scenario: 执行填充复用预览确认的候选
- **GIVEN** 用户已完成预览并确认某行使用指定的 `SpecId`
- **WHEN** 用户执行填充
- **THEN** 系统使用该已确认的 `SpecId` 进行填充
- **AND** 不在执行阶段重新选择其它默认候选

#### Scenario: 执行填充复用预览确认的建议结果
- **GIVEN** 用户已完成预览并确认某行使用 LLM 建议结果
- **WHEN** 用户执行填充
- **THEN** 系统使用该已确认的建议内容进行填充
- **AND** 不在执行阶段重新计算新的默认建议

### Requirement: 多阶段配置的向后兼容
系统 SHALL 在未提供多阶段参数时保持当前单阶段行为兼容。

#### Scenario: 旧调用方不传新参数
- **GIVEN** 调用方仍按旧版请求格式发起匹配请求
- **WHEN** 系统执行匹配
- **THEN** 系统使用默认单阶段策略
- **AND** 匹配行为与现有版本保持一致
