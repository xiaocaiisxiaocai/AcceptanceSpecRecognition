## ADDED Requirements

### Requirement: 表格预览支持有界行列窗口

系统 MUST 支持按行列偏移和数量返回表格预览窗口，并在服务端执行明确上限，避免单次预览无界加载整张工作表。

#### Scenario: 加载首个预览窗口

- **WHEN** 客户端提交有效的行列 offset 和 window size
- **THEN** 系统只返回该窗口内的 `rows` 与 `structuredRows`
- **AND** 响应包含实际 offset、总行数和总列数

#### Scenario: 加载尾部窗口

- **WHEN** 请求窗口跨过表格最后一行或最后一列
- **THEN** 系统返回范围内实际存在的数据
- **AND** 不填充伪造行列或越界读取

#### Scenario: 请求无效或过大窗口

- **WHEN** offset 为负数或行列数量超过服务端上限
- **THEN** 系统返回明确的参数错误或按公开契约截断
- **AND** 不执行无界预览

#### Scenario: 纯文本与结构化窗口保持一致

- **WHEN** 表格窗口包含嵌套表格或其他结构化单元格
- **THEN** `rows` 与 `structuredRows` 表达相同的行列窗口
- **AND** 两者 offset 和实际尺寸一致
