## Context
新增“文件对比”功能，仅支持同类型文件：Word 对 Word、Excel 对 Excel。暂不支持 PDF。

## Goals / Non-Goals
- Goals: 提供可预览的差异列表与高亮展示；Excel 全工作簿自动对比。
- Non-Goals: 跨类型对比、PDF 视觉比对、复杂格式还原。

## Decisions
- 统一抽取为“对比块（block）”后执行文本 diff。
  - Word：按表格单元格抽取块，保留表格/行列位置。
  - Excel：按工作簿全部 Sheet、行列单元格抽取块（含 A1 位置）。
- Diff 算法优先使用 Myers/Patience 等通用序列 diff（实现简单、可解释）。
- 结果模型包含：块 ID、位置、原值/新值、差异类型（新增/删除/修改）。

## Risks / Trade-offs
- 文本抽取会损失部分格式信息，但能快速提供业务可用的差异提示。

## Migration Plan
- 无数据库变更；新增 API 与页面，不影响现有功能。

## Open Questions
- 是否需要导出对比报告（HTML/PDF）作为后续扩展项。
