# Table Preview Capability

## Purpose
提供表格预览的结构化数据输出，确保嵌套表格等复杂单元格内容可被前端完整展示、定位、回显与后续对比使用。

## Requirements

### Requirement: 表格预览返回结构化数据
系统 SHALL 在表格预览接口中同时返回纯文本与结构化数据。

#### Scenario: 兼容旧字段
- **WHEN** 前端调用表格预览接口
- **THEN** 响应包含 `rows`（纯文本二维数组）
- **AND** 同时包含 `structuredRows`（结构化单元格二维数组）

#### Scenario: 嵌套表格结构化
- **GIVEN** 单元格内存在嵌套表格
- **WHEN** 系统返回结构化单元格
- **THEN** 该单元格以 `table` 结构表达嵌套表格的行列与内部单元格
- **AND** 前端可选择以 JSON 或表格方式展示
