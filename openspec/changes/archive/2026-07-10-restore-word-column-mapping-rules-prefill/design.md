## Context
- 当前智能填充主链已经稳定为 Embedding 召回、证据重排、AI 复核与等价裁决。
- 用户仍然需要在 Word 多表格场景下，通过列映射规则快速完成“项目 / 规格 / 验收 / 备注”列的预填。
- 这两条链路并不冲突：列映射规则解决的是“源表结构定位”，AI 解决的是“语义匹配与决策”。

## Goals / Non-Goals
- Goals:
  - 恢复可维护的全局列映射规则配置能力
  - 让 Word 导入和智能填充自动预填列索引
  - 保留用户逐表手动调整能力
- Non-Goals:
  - 不让列映射规则参与任何 AI 匹配决策
  - 不让 Excel 复用这套规则
  - 不引入按客户/制程分作用域的复杂规则集

## Decisions
### Decision: 规则持久化和配置页恢复
- 恢复 `ColumnMappingRules` 表、实体、仓储、控制器和配置页。
- 仍使用“目标字段 + 匹配模式 + 匹配词 + 优先级 + 启用状态”的最小模型。

### Decision: 自动预填逻辑保持在前端
- 前端读取 `/api/column-mapping-rules/effective` 后，在本地对 Word 表头执行匹配并生成默认列位。
- 这样可以同时服务导入页与 smart-fill，避免把预填逻辑耦合到不同后端接口。

### Decision: 仅 Word 使用规则
- `Word` 使用表头规则自动预填。
- `Excel` 继续保持现有工作表级手工列配置，避免误导和错误命中。

## Risks / Trade-offs
- 旧“彻底移除列映射规则”的测试与 OpenSpec 会失效，需要同步修正。
- 若规则命中冲突，仍可能出现个别表格预填不理想，因此必须保留手工调整。

## Migration Plan
1. 新增迁移重新创建 `ColumnMappingRules` 表。
2. 恢复 API、前端配置页和导航入口。
3. 在 Word 导入与 smart-fill 页面恢复自动预填逻辑。
4. 更新测试和 OpenSpec，使“Word 预填、Excel 不使用”成为当前真相。
