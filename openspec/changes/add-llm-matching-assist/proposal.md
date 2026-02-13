# Change: 添加 LLM 参与的匹配复核与生成建议

## Why
当前匹配主要依赖文本相似度与 Embedding，面对语义差异较大的规格时可能产生低置信度结果。引入 LLM 参与复核与生成，可提升匹配质量，并在无匹配时给出可解释的补全建议。

## What Changes
- 仅返回最佳匹配（Top‑1），彻底取消候选列表与 Top‑K 配置项。
- 在无匹配时返回明确原因（如范围内无候选、得分低于阈值、LLM降级说明）。
- 新增 LLM 复核评分/理由（可选开关），并支持 SSE 流式输出与实时展示。
- 在低置信度/无匹配时，新增 LLM 生成验收/备注建议（可选开关），并支持 SSE 流式输出。
- 新增/扩展 Prompt 模板与占位符，支持复核与生成场景。

## Impact
- **Affected specs**:
  - matching-engine
  - user-interface
- **Affected code**:
  - Core：LLM 连接器/服务、匹配复核与生成流程
  - API：MatchingController、DTOs、Prompt 模板读取
  - Web：智能填充页面配置与结果展示
  - Tests：API/匹配流程相关用例
