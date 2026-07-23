## ADDED Requirements
### Requirement: 列映射规则管理 API
系统 SHALL 提供列映射规则管理 API，用于维护 Word 表头自动预填所需的全局规则。

#### Scenario: 读取生效规则
- **WHEN** 客户端访问 `/api/column-mapping-rules/effective`
- **THEN** 系统返回当前所有启用规则
- **AND** 结果按目标字段与优先级排序

#### Scenario: 管理规则 CRUD
- **WHEN** 客户端访问 `/api/column-mapping-rules`
- **THEN** 系统支持列映射规则的查询、新增、更新和删除
- **AND** 对非法正则表达式或空匹配词返回明确错误
