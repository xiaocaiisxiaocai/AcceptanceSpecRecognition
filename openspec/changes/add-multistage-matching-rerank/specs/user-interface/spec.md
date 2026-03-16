## MODIFIED Requirements

### Requirement: 智能填充界面
系统 SHALL 提供智能匹配与文档填充的 Web 界面，并支持单阶段与多阶段匹配策略配置。

#### Scenario: 匹配参数配置
- **WHEN** 用户进入匹配配置步骤
- **THEN** 系统提供 Embedding 服务、LLM 服务、匹配阈值等基础配置
- **AND** 系统提供 `SingleStage` / `MultiStage` 策略选择

#### Scenario: 多阶段高级参数展示
- **GIVEN** 用户将匹配策略切换为 `MultiStage`
- **WHEN** 页面渲染高级配置区域
- **THEN** 系统显示召回数量、重排模式、歧义阈值等高级参数
- **AND** 默认保持现有单阶段配置隐藏状态不变

#### Scenario: 匹配预览
- **WHEN** 用户执行预览
- **THEN** 系统显示每行的匹配结果、得分与置信度状态
- **AND** 在启用多阶段时展示当前策略与必要的重排说明

#### Scenario: 执行填充使用预览确认结果
- **GIVEN** 用户已在预览中确认每行使用的匹配结果或建议结果
- **WHEN** 用户点击执行填充
- **THEN** 系统使用这些已确认结果执行填充
- **AND** 页面不会在未重新预览确认的情况下静默替换为新的默认匹配

## ADDED Requirements

### Requirement: 歧义状态提示
系统 SHALL 在预览界面中提示高歧义样本状态。

#### Scenario: 预览中标记歧义样本
- **GIVEN** 某行匹配结果在多阶段重排后仍属于高歧义状态
- **WHEN** 系统展示预览结果
- **THEN** 页面显示该行属于歧义样本
- **AND** 页面提示该行是否已触发 LLM 复核或仍需人工关注
