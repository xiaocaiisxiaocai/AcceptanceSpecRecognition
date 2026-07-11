## ADDED Requirements
### Requirement: 普通确认不自动沉淀表名路由规则
系统 SHALL 在用户确认普通智能结构识别结果后保存结构模板和列映射学习，但不得默认生成基于表名或 Sheet 名的客户级学习路由规则。

#### Scenario: Excel 验收表确认
- **GIVEN** 用户确认了一张 Excel 验收规格表
- **AND** 该 Sheet 有名称
- **WHEN** 系统执行学习保存
- **THEN** 系统保存或更新 `DocumentTemplate`
- **AND** 系统保存列映射学习规则
- **AND** 系统不创建 `MatchScope = TableName` 且 `Source = Learned` 的路由规则

#### Scenario: Word 表格确认
- **GIVEN** 用户确认了一张 Word 文档中的表格
- **WHEN** 系统执行学习保存
- **THEN** 系统保存或更新该表的结构模板
- **AND** 系统不基于文件名、表格序号、段落标题或表格附近文本创建表名路由规则

### Requirement: 手工路由规则继续持久化
系统 SHALL 保留人工维护的智能结构路由规则持久化能力，用于少数明确的跳过、推荐或覆盖场景。

#### Scenario: 管理员新增辅助表跳过规则
- **WHEN** 管理员在配置页新增一条智能结构辅助规则
- **THEN** 系统将规则保存到 `SmartStructureRoutingRules`
- **AND** 后续识别可继续按该规则匹配

#### Scenario: 历史学习表名规则不批量删除
- **GIVEN** 数据库中已经存在历史 `Learned` 表名路由规则
- **WHEN** 系统升级到新行为
- **THEN** 系统不执行批量删除或批量禁用
- **AND** 是否清理历史规则由后续显式维护流程处理
