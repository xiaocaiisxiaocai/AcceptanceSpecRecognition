# 智能结构识别 P2 收口设计

## 目标

按顺序完成仅规格门禁补漏、列映射规则运行时语义统一、列语义召回可维护性收敛和测试守卫加固。P2-D 的 DTO/大型测试设施重构不在本次范围内。

## 决策

### 1. 仅规格必须显式确认

- 首次规则识别得到 `NeedConfirm + IsSpecificationOnly` 时，不默认参与导入。
- 用户在智能结构确认卡确认后，前端将该表替换为 `AutoApply`，此时才允许生成仅规格导入配置。
- 模板命中或健康检查真正得到 `AutoApply` 的仅规格表仍可直接使用。
- 自动候选判断同时检查未映射且有样本数据的列；存在此类疑似项目列时不自动标记为仅规格。

### 2. 列映射规则保留结构化语义

- Application 将数据库规则映射为 Core 的结构化规则，保留目标字段、`Equals/Contains/Regex`、优先级和客户范围。
- Core 提供唯一匹配器：`Equals` 精确匹配，`Contains` 保留当前包含及编辑距离能力，`Regex` 使用显式超时。
- 规则识别、表头信号判断、仅规格判断和缺口分析器复用同一匹配语义。
- 不只修改统计器，否则配置页的 Equals/Regex 仍会是假配置。

### 3. 列语义召回策略统一

- Core 提供统一的“方法列/结果信号”判定，规则识别和 Application 结果校验共同使用。
- 同一列只保留最高置信度的一条建议；同一业务字段仍只保留一条建议。
- 新增 `SmartConfigColumnSemanticRecall` Prompt 模板场景，复用现有数据库模板初始化、管理、预览和恢复默认流程。
- 新增独立列召回超时配置，默认保持 20 秒；整单共享 LLM 总预算继续生效。
- LLM JSON 解析显式释放 `JsonDocument`；不为轻微的二次解析成本重写整套编排。

### 4. 测试守卫只修真实失焦

- 列宽测试只检查“优先级”列片段。
- 执行历史 C# 回归测试直接检查回放组件，不再检查无关的智能填充详情弹窗。
- G5 测试名称改为通用系统元数据语义。

## OpenSpec 边界

这些修改分别恢复已有变更的既定设计，不新增独立产品能力：

- `add-specification-only-import-project-backfill`：补齐显式确认和疑似项目列门禁。
- `migrate-column-mapping-keywords-to-db`：让 MatchMode 与原设计一致。
- `add-smart-structure-column-semantic-recall`：补齐重复列校验、模板化和独立超时。

在对应 change 的 design/tasks 中追加回归任务并执行严格校验，不创建重复提案。

## 验证

- 每个行为先写失败测试，再实现最小修复。
- 分批运行 Core/API/Node/Vitest 定向测试。
- 最终运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`、`pnpm --dir web test`、`pnpm --dir web typecheck`。

