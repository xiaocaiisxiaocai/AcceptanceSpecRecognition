# Design: 仅规格导入项目回填

## Context
系统已有 `IsSpecificationOnly` 结构标记和智能填充 `MatchingMode.SpecificationOnly`。但正式导入接口仍要求项目列和规格列。用户确认阶段 4 的业务口径为：缺项目导入时不留空、不写占位词，而是 `Project = Specification`、`Specification = Specification`。

关键风险是“未识别到项目列”不等于“确实没有项目列”。因此本变更的核心不是放宽所有缺项目导入，而是增加保守门禁。

## Goals / Non-Goals
- Goals:
  - 允许明确仅规格表进入历史规格库。
  - 缺项目导入时用规格值补项目值。
  - 对疑似漏识别项目列保持人工确认。
  - Word 和 Excel 使用同一业务规则。
  - 保持既有查重、覆盖、Embedding 语义尽量复用现有 `Project + Specification` 链路。
- Non-Goals:
  - 不新增数据库字段。
  - 不允许 `Project` 为空入库。
  - 不自动把所有缺项目列的表当作仅规格表。
  - 不改变智能填充默认匹配模式。

## Decisions
- Decision: 落库时复制规格到项目。
  - Reason: 与用户确认的业务口径一致，也能复用现有 `Project + Specification` 查重、索引和匹配代码。
- Decision: 仅两类来源允许补项目。
  - Reason: 自动来源必须是结构识别明确 `IsSpecificationOnly=true` 且健康检查通过；人工来源必须是用户确认仅规格。
- Decision: 疑似项目列时不补项目。
  - Reason: 若存在项目列但被漏识别，复制规格会污染历史项目字段，后续匹配和查重都会放大错误。
- Decision: 不新增持久化“项目来源”字段。
  - Reason: 第一版先闭环导入能力；需要报表区分真实项目/回填项目时再单独扩展数据模型。

## Eligibility Gate
系统只有满足全部条件时，才可以自动启用规格补项目：
1. `ProjectColumnIndex` 为空。
2. `SpecificationColumnIndex` 存在。
3. `IsSpecificationOnly=true`，且来源为模板、规则健康检查或用户确认。
4. 表头和样本中没有高风险项目候选列。
5. 规格列数据健康：非空率达标，且不像短项目分类列。
6. 无项目/规格疑似判反、重复列、越界列、数据区异常。

如果任一条件不满足，系统必须进入 `NeedConfirm`，由用户手动选择项目列或确认仅规格。

## Data Flow
1. 智能识别返回表结构。
2. 数据导入页将 `IsSpecificationOnly` 和列映射带入确认配置。
3. 后端导入预览构造行数据：
   - 若有项目列：按项目列读取 `Project`。
   - 若无项目列且允许仅规格补项目：读取规格列作为 `Specification`，并复制给 `Project`。
   - 若无项目列且不允许补项目：返回校验错误或待确认状态。
4. 导入重复检测接收补齐后的行，继续按 `Project + Specification` 执行规则和 AI 检测。
5. 最终导入写入 `AcceptanceSpec.Project = Specification`。

## Risks / Trade-offs
- Risk: 误把漏识别项目列当作仅规格表。
  - Mitigation: 疑似项目列门禁、健康检查、用户确认。
- Risk: 历史数据中项目字段与规格字段相同，影响列表阅读。
  - Mitigation: 前端导入确认提示；必要时后续再新增来源标记。
- Risk: 同一规格被不同真实项目复用时，仅规格导入会在查重上合并。
  - Mitigation: 这是用户选择仅规格导入的业务取舍；导入确认需提示风险。

## Test Plan
- Excel 明确仅规格导入：`Project` 写入规格值。
- Word 明确仅规格导入：`Project` 写入规格值。
- 疑似存在项目列时不得自动补项目。
- 用户手动确认仅规格后允许补项目。
- 导入重复检测按补齐后的 `规格 + 规格` 命中已有记录。
- 规格列为空或数据区异常时拒绝补项目。

## 2026-07-10 回归收口

- 首次规则识别产生 `NeedConfirm + IsSpecificationOnly` 时，前端不得默认选中参与导入；只有模板/健康检查明确自动采用，或用户在确认卡显式确认后，才生成仅规格导入配置。
- 自动候选判断除项目关键词外，还要拦截未映射且存在样本数据的列，避免把词表外项目列静默忽略。
