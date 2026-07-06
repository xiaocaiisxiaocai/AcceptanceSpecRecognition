# Design: 智能结构识别自适应表格路由

## Context
当前链路是“客户模板命中 -> 规则识别 -> 健康检查 -> LLM 结构裁决 -> 用户确认学习”。这条链路解决了表头和字段识别，但真实 Excel 往往包含大量非验收表。继续堆关键词会提高误报风险，直接放大 LLM 调用也会增加成本和不稳定性。

## Goals
- 对每张表给出业务类型判断和推荐级别。
- 文档级排序推荐最值得用户处理的表。
- NeedConfirm、Skip、AutoApply 都要给出可解释原因。
- 用户确认行为能提升后续相似结构识别质量。
- 保持 Word/Excel 共用模型，保留手动配置兜底。

## Non-Goals
- 不移除现有 `DocumentTemplate` 与 `ColumnMappingRule` 学习机制。
- 不强制所有客户共享结构模板。
- 不要求第一版做视觉截图级 UI 重设计。

## Core Model
识别结果新增以下概念：

- `TableKind`：`AcceptanceSpec`、`SafetySpec`、`EnvironmentalSpec`、`SecsSpec`、`Utility`、`Quotation`、`Layout`、`BomOrSpareParts`、`SignatureOrCover`、`Unknown`。
- `Recommendation`：`Recommended`、`Optional`、`NeedConfirm`、`Skip`。
- `RecognitionIssues`：结构化问题列表，包含编码、严重度、字段、说明。
- `RankingScore`：文档级候选表排序分，不等同于字段映射置信度。

## Data Flow
1. 解析 Word/Excel 得到扁平表格。
2. 对每张表执行轻量表格类型识别，基于表名、表头、数据区样本、字段完整性和历史案例信号。
3. 执行现有模板/规则/健康检查流程。
4. 将规则候选、健康检查问题、表格类型分和历史案例分合并为 `RankingScore` 与 `Recommendation`。
5. LLM 预算按候选价值排序分配，优先处理高可能验收表且规则不确定的灰区。
6. 返回前端按推荐分组展示。
7. 用户确认后更新模板使用次数、最近确认时间和结构案例信号。

## Key Decisions
### Decision 1: 先做规则+历史案例的类型识别，不依赖 Embedding
第一版使用可解释的轻量特征，避免新增 AI 服务依赖。Embedding 可以作为后续增强，但不能成为上传识别的前置条件。

### Decision 2: AutoApply 继续保守
类型识别只影响推荐和跳过建议，不直接绕过现有健康检查。只有字段映射、数据区、置信度和结构健康都通过时才允许 AutoApply。

### Decision 3: Skip 是建议，不是删除
建议跳过的表仍保留在响应中，前端默认折叠。用户可以手动展开并改为导入，避免误跳过。

### Decision 4: 学习记录使用权重而非绝对覆盖
同客户、近期开启、使用次数高、表名/表头相似的案例权重更高；旧案例不删除，但权重自然下降。

## Risks
- 表格类型识别误判：通过默认 `NeedConfirm` 和可展开跳过表降低风险。
- 响应契约变大：新增字段保持向后兼容，旧前端可忽略。
- 历史案例污染：用户确认行为需要记录是否手动改动，低质量案例不应快速提升为全局规则。

## Testing
- Core：表格类型识别、推荐级别、排序分、问题编码。
- API：Excel 多 sheet 混合样本返回推荐/跳过分组；Word 主表不回退。
- Learning：确认后案例权重影响后续相似表排序，但不绕过健康检查。
- Frontend：确认卡分组展示、跳过表可展开、NeedConfirm 原因显示。
- Real sample：继续使用 PA06 Excel 与太阳式翻板暂存机 Word 作为回归样本。
