## ADDED Requirements

### Requirement: 智能结构识别返回表格推荐信息
系统 SHALL 在智能结构识别 API 响应中为每张表返回表格类型、推荐级别、排序分和结构化原因。

#### Scenario: 返回推荐字段
- **GIVEN** 用户已上传 Word 或 Excel 文件
- **WHEN** 前端调用 `POST /api/smart-config/recognize`
- **THEN** 每个表格结果包含 `tableKind`
- **AND** 每个表格结果包含 `recommendation`
- **AND** 每个表格结果包含 `rankingScore`
- **AND** 每个表格结果包含结构化 `issues`

#### Scenario: 建议跳过仍保留表格结果
- **GIVEN** 某张表被识别为报价、Layout、Utility、备品清单或签核页
- **WHEN** API 返回识别结果
- **THEN** 该表仍出现在 `tables` 数组中
- **AND** `recommendation` 为 `Skip`
- **AND** 响应包含用户可读的跳过原因

### Requirement: 智能结构识别新增字段保持兼容
系统 MUST 保持智能结构识别 API 的既有字段兼容，新增推荐字段不得破坏旧流程。

#### Scenario: 旧字段仍可用于导入配置
- **WHEN** 前端只读取既有表头、行范围、字段列索引和决策字段
- **THEN** 新增推荐字段不会改变这些既有字段的含义
- **AND** 旧的手动配置兜底流程仍可使用

### Requirement: 智能结构路由规则配置 API
系统 SHALL 提供智能结构路由规则配置 API，用于人工维护、客户级隔离和确认学习结果审阅。

#### Scenario: 管理路由规则
- **WHEN** 前端调用智能结构路由规则 API
- **THEN** 系统支持查询、新增、更新、删除路由规则
- **AND** 规则字段包含名称、表格类型、推荐结果、匹配范围、匹配方式、匹配词、权重、优先级、启停状态、来源和客户域

#### Scenario: 查询客户有效规则
- **GIVEN** 系统存在全局规则和客户级规则
- **WHEN** 前端或识别服务按客户查询有效规则
- **THEN** API 返回启用的全局规则和该客户规则
- **AND** 其他客户的客户级规则不得出现在结果中
