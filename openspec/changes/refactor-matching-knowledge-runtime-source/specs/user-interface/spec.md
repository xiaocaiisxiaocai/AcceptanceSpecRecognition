## MODIFIED Requirements
### Requirement: 配置管理界面
系统 SHALL 提供 AI 服务、Prompt 模板与列映射规则等现行 Web 配置页面，不再提供 matching-knowledge 配置页。

#### Scenario: 访问旧 matching-knowledge 页面
- **WHEN** 用户尝试访问旧的 matching-knowledge 配置页面
- **THEN** 系统不再提供该页面
- **AND** 不再展示“清空当前配置”“恢复默认配置”或“AI 草稿导入”等旧操作
