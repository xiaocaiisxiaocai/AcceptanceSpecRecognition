## ADDED Requirements

### Requirement: 表格预览返回结构化 JSON
系统 SHALL 在“表格预览”接口中返回结构化 JSON，用于表达单元格内的复杂内容（例如嵌套表格），并便于前端显示与后续对比。

#### Scenario: 兼容旧字段
- **WHEN** 前端调用表格预览接口
- **THEN** 响应同时包含 `rows`（纯文本二维数组）
- **AND** 同时包含 `structuredRows`（结构化单元格二维数组）

#### Scenario: 嵌套表格结构化
- **GIVEN** 单元格内存在嵌套表格
- **WHEN** 系统返回结构化单元格
- **THEN** 该单元格以 `table` 结构表达嵌套表格的行列与内部单元格
- **AND** 前端可以选择以 JSON 或表格方式展示

