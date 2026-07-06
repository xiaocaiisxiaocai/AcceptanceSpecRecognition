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
