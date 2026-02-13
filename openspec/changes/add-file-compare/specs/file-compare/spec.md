## ADDED Requirements

### Requirement: 文件对比能力
系统 SHALL 提供同类型文件对比能力，支持 Word 与 Excel。

#### Scenario: Word 同类型对比
- **WHEN** 用户上传两个 Word 文件并发起对比
- **THEN** 系统返回差异结果（新增/删除/修改）

#### Scenario: Excel 全工作簿对比
- **WHEN** 用户上传两个 Excel 文件并发起对比
- **THEN** 系统自动对比所有工作表并返回差异结果

---

### Requirement: 对比结果预览
系统 SHALL 提供对比结果的结构化预览数据。

#### Scenario: 差异列表
- **WHEN** 对比完成
- **THEN** 响应包含差异列表、差异类型与位置信息

