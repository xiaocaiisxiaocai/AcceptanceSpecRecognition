## MODIFIED Requirements

### Requirement: 来源文件记录支持 Excel
系统 SHALL 记录导入数据的来源文件信息，并支持 Excel 文件作为来源。

#### Scenario: 记录 Excel 文件来源
- **GIVEN** 用户从 Excel 导入了验收规格数据
- **WHEN** 系统写入 `AcceptanceSpec` 数据
- **THEN** 每条数据均关联到来源文件记录
- **AND** 来源文件记录包含文件名、哈希、上传时间与文件类型（包含 Excel）

