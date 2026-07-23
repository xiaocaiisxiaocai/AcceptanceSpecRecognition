## MODIFIED Requirements

### Requirement: 表格预览返回结构化数据

系统 SHALL 将工作表解析为有界预览，并同时返回当前窗口的纯文本与结构化数据、偏移和总行列数。

#### Scenario: 兼容窗口内字段

- **WHEN** 前端调用表格预览接口并提供有效窗口
- **THEN** 响应包含当前窗口的 `rows`（纯文本二维数组）
- **AND** 同时包含当前窗口的 `structuredRows`（结构化单元格二维数组）

#### Scenario: 嵌套表格结构化

- **GIVEN** 当前窗口的单元格内存在嵌套表格
- **WHEN** 系统返回结构化单元格
- **THEN** 该单元格以 `table` 结构表达嵌套表格的行列与内部单元格
- **AND** 前端可选择以 JSON 或表格方式展示

#### Scenario: 请求有效预览窗口

- **WHEN** 用户请求正数且不超过服务端上限的行列窗口
- **THEN** 系统返回该窗口的结构化数据与总行列数

#### Scenario: 请求无界或超限预览

- **WHEN** 客户端传入 `previewRows <= 0`、`previewColumns <= 0` 或超过服务端上限
- **THEN** 系统返回 `400 Bad Request`
- **AND** 不读取或物化完整工作表作为兼容行为
