## ADDED Requirements
### Requirement: 智能结构辅助规则页面降低表名误导
系统 SHALL 在智能结构路由规则配置页明确表达该页面用于辅助排除或覆盖推荐结果，而不是主结构识别配置入口。

#### Scenario: 页面展示辅助定位说明
- **WHEN** 用户进入智能结构路由规则配置页
- **THEN** 页面说明系统默认按表头结构和列映射识别
- **AND** 页面说明本页仅用于少数强制跳过、推荐覆盖或人工兜底场景

#### Scenario: 表名匹配标注 Excel 兜底
- **WHEN** 用户选择匹配范围
- **THEN** `TableName` 选项显示为“Sheet 名/表名（仅 Excel 兜底）”或等价中文说明

### Requirement: 智能结构辅助规则默认优先表头匹配
系统 SHALL 在新增智能结构辅助规则时默认使用表头匹配，避免引导用户按客户自定义 Sheet 名建立规则。

#### Scenario: 新增规则默认表头
- **WHEN** 用户点击新增智能结构辅助规则
- **THEN** 表单默认 `matchScope` 为 `Headers`
- **AND** 不默认使用 `TableName`

#### Scenario: 表格类型中文显示
- **WHEN** 页面展示或编辑表格类型
- **THEN** 页面使用中文标签展示常见类型
- **AND** 请求载荷中的内部枚举值保持兼容
