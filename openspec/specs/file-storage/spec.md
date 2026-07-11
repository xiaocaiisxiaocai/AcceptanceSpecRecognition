# File Storage Capability

## Purpose
定义文档上传与填充结果的文件系统存储规则，确保上传文件与生成文件在服务器端可追踪、可下载、可校验并可清理。
## Requirements
### Requirement: 上传文件存储
系统 SHALL 将上传文件存储在服务器文件系统中。

#### Scenario: Word文件上传存储
- **WHEN** 用户上传 Word 文档
- **THEN** 系统将文件保存到uploads/word-files/{date}/{guid}.docx

#### Scenario: Excel文件上传存储
- **WHEN** 用户上传 Excel 文档
- **THEN** 系统将文件保存到uploads/excel-files/{date}/{guid}.xlsx

---

### Requirement: 填充文件存储
系统 SHALL 保存填充后的结果文件，或将回写后的源文件重新落盘。

#### Scenario: Word填充结果落盘
- **WHEN** 系统生成填充后的 Word 文档
- **THEN** 系统将文件保存到uploads/filled-files/{date}/{guid}.docx

#### Scenario: Excel回写结果落盘
- **WHEN** 系统完成 Excel 源文件回写
- **THEN** 系统将更新后的文件保存到uploads/excel-files/{date}/{guid}.xlsx

---

### Requirement: 文件清理
系统 SHALL 在删除文件记录时清理对应物理文件。

#### Scenario: 删除文件记录
- **WHEN** 用户删除文件记录且无关联验收规格
- **THEN** 系统删除对应物理文件

### Requirement: 批量回复临时文件存储
系统 SHALL 为批量回复流程保存来源文档、目标文档和执行结果所需的临时文件，并在会话结束或过期后清理这些文件。

#### Scenario: 保存批量回复临时上传文件
- **WHEN** 用户在批量回复页面上传来源文档和目标文档
- **THEN** 系统将这些文件保存到服务器临时存储位置
- **AND** 这些文件不作为正式导入文档写入长期业务主流程

#### Scenario: 清理过期临时文件
- **WHEN** 批量回复会话执行完成或会话超过保留时间
- **THEN** 系统清理对应的临时来源文件、临时目标文件和中间产物
- **AND** 不影响最终可下载结果文件的生命周期

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
