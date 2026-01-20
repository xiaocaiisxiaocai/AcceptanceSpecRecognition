# File Storage Capability

## Purpose
定义文档上传与填充结果的文件系统存储规则，确保上传文件与生成文件在服务器端可追踪、可下载、可校验并可清理。

## Requirements

### Requirement: 上传文件存储
系统 SHALL 将上传文件存储在服务器文件系统中。

#### Scenario: Word文件上传存储
- **WHEN** 用户上传Word文档
- **THEN** 系统将文件保存到uploads/word-files/{date}/{guid}.docx

#### Scenario: Excel文件上传存储
- **WHEN** 用户上传Excel文档
- **THEN** 系统将文件保存到uploads/excel-files/{date}/{guid}.xlsx

---

### Requirement: 填充文件存储
系统 SHALL 保存填充后的Word文档文件。

#### Scenario: 填充文件落盘
- **WHEN** 系统生成填充后的文档
- **THEN** 系统将文件保存到uploads/filled-files/{date}/{guid}.docx

---

### Requirement: 文件清理
系统 SHALL 在删除文件记录时清理对应物理文件。

#### Scenario: 删除文件记录
- **WHEN** 用户删除文件记录且无关联验收规格
- **THEN** 系统删除对应物理文件
