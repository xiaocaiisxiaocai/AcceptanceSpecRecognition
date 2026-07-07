## 1. Specification
- [x] 1.1 校验 OpenSpec 提案：`openspec validate refine-smart-structure-routing-rules --strict`。
- [x] 1.2 获得实施批准后再修改业务源码。

## 2. Backend
- [x] 2.1 调整 `SmartConfigurationLearningService`，普通确认不再自动生成 `TableName` 学习路由规则。
- [x] 2.2 保留列映射学习和 `DocumentTemplate` 结构模板保存。
- [x] 2.3 将历史案例排序中的表名相似度降为弱信号，确保表头相似度优先。
- [x] 2.4 保持人工 `SmartStructureRoutingRule` 匹配逻辑和 API 不变。

## 3. Frontend
- [x] 3.1 将路由规则配置页文案调整为辅助规则/排除覆盖语义。
- [x] 3.2 表格类型下拉显示中文，内部值保持现有枚举。
- [x] 3.3 新增规则默认 `matchScope` 改为 `Headers`。
- [x] 3.4 将 `TableName` 展示为“Sheet 名/表名（仅 Excel 兜底）”。

## 4. Tests
- [x] 4.1 补后端测试：Excel 普通确认不生成表名学习路由规则。
- [x] 4.2 补后端测试：Word 普通确认不生成表名学习路由规则。
- [x] 4.3 补后端测试：结构模板仍保存并可被相似表头复用。
- [x] 4.4 补后端测试：手工跳过规则仍能命中辅助表。
- [x] 4.5 补前端测试：页面默认值、中文类型显示和 TableName 兜底说明。

## 5. Verification
- [x] 5.1 运行智能结构相关后端测试。
- [x] 5.2 运行前端相关测试或类型检查。
