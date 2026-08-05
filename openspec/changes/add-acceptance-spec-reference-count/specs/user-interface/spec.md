## ADDED Requirements

### Requirement: 验收规格列表与详情显示引用次数

系统 SHALL 在验收规格管理界面展示后端返回的当前内容版本引用次数。

#### Scenario: 列表显示引用次数
- **WHEN** 用户打开验收规格列表
- **THEN** 每行显示该规格的引用次数
- **AND** 数值与列表 API 返回值一致

#### Scenario: 详情显示引用次数
- **WHEN** 用户打开验收规格详情
- **THEN** 详情区域显示该规格的引用次数
- **AND** 数值与详情 API 返回值一致
