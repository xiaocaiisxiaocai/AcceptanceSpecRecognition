## Context
智能填充完整回放包含源行文本、最佳匹配、候选列表、证据、问题项、AI 等价裁决、人工确认与最终写回值。该结构随行数、候选数和文本长度增长，没有稳定的“最大行数”边界。

现有 `ExecutionHistoryRecord.DetailJson` 适合承载列表摘要和轻量详情，不适合承载大体积完整回放。继续压缩该字段只能在“可存储”和“可查看完整明细”之间取舍。

## Goals / Non-Goals
- Goals:
  - 每一行匹配都能在执行记录中查看完整明细。
  - 执行记录列表与详情初始加载保持轻量。
  - 避免新增数据库表和迁移，优先复用现有文件存储。
  - 不重新匹配、不重新调用 AI，只读取执行时归档的事实数据。
- Non-Goals:
  - 不为历史已压缩记录补算完整明细。
  - 不把完整回放拆成关系型明细表。
  - 不改变智能填充匹配与执行决策逻辑。

## Decisions
- Decision: 完整回放保存到文件系统归档。
  - Why: 项目已有 `FileStorage` 本地目录和容器卷语义，文件内容优先落地文件系统是现有约定；相比新增表，迁移风险更低。
  - Details: 归档路径使用 `uploads/execution-history/smart-fill/{date}/{guid}.json.gz` 或等价目录；`DetailJson` 只保存相对路径、归档状态和轻量行索引。

- Decision: `DetailJson` 继续保留轻量回放。
  - Why: 执行记录详情页需要快速展示文件、Sheet、行状态、标签、置信度与最终写回概览。
  - Details: 当完整明细超过阈值时，轻量回放可以剥离候选与长文本，但必须保留定位字段，确保前端能按行请求完整详情。

- Decision: 新增只读回放详情接口。
  - Why: 前端不应一次性下载超大完整归档，行详情弹窗或抽屉按需加载更稳定。
  - Candidate API:
    - `GET /api/execution-history/{id}/smart-fill/playback`
    - `GET /api/execution-history/{id}/smart-fill/rows?fileIndex=0&sheetIndex=0&rowIndex=12`
  - 实现时可以先做按行接口；完整接口用于调试或导出，非首要。

- Decision: 归档路径不新增数据库列，写入 `DetailJson` 元数据。
  - Why: 本次目标是修复可查看完整明细，不需要数据库结构变更。
  - Trade-off: 如果未来需要对行级明细做数据库查询、审计或统计，可再引入分片表。

## Risks / Trade-offs
- 完整归档文件可能较大。
  - Mitigation: 使用 gzip，前端按行懒加载。
- 归档文件丢失会导致完整明细不可读。
  - Mitigation: 详情页保留轻量回放，接口返回明确错误，页面提示归档缺失。
- 旧记录没有完整归档。
  - Mitigation: 保持 legacy 或 slimmed 降级展示，不伪装成完整明细。

## Migration Plan
1. 扩展文件存储服务，支持保存和读取执行历史归档文件。
2. 保存智能填充执行记录时，先写完整回放归档，再把 `DetailJson` 缩减为轻量索引和归档元数据。
3. 增加完整行详情读取 API，并按当前用户公司与用户边界校验执行记录归属。
4. 前端在行详情打开时调用新 API，拿不到归档时显示明确降级提示。
5. 补充大记录回归测试，验证 `DetailJson` 低于阈值且任意行仍可读取完整候选与证据。

## Open Questions
- 是否需要提供“下载完整回放 JSON”的运维入口。
- 归档文件保留周期是否跟随执行记录生命周期，还是需要单独清理策略。
