## ADDED Requirements

### Requirement: 智能填充执行历史归档文件存储
系统 SHALL 将超出执行记录详情承载范围的智能填充完整回放保存为服务器文件系统归档。

#### Scenario: 保存完整回放归档
- **WHEN** 智能填充执行记录包含完整回放数据
- **THEN** 系统将完整回放写入 `uploads/execution-history/smart-fill/{date}/` 下的归档文件
- **AND** 执行记录详情中仅保存可用于读取该归档的相对路径或等价元数据

#### Scenario: 读取归档路径防逃逸
- **WHEN** 系统根据执行记录中的归档路径读取文件
- **THEN** 路径必须解析在文件存储根目录内
- **AND** 非法路径会被拒绝读取
