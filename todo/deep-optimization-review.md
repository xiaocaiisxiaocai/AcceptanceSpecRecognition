# 深度优化评审（2026-06-12）

> 背景：等价裁决决策层修复已完成（见 architecture-improvements.md 已勾选项），vLLM Docker 部署进行中。
> 本文是面向"更智能、更快、更准"的下一阶段路线图，全部结论经源码核对，标注置信度与工作量。
> 优先级原则：准确性 > 智能 > 速度 > 其他。

---

## A. 准确性

### A1. vLLM Structured Outputs：从根上消灭 JSON 解析失败 ⭐🟢（vLLM 上线后首做）
- **现状**：LLM 输出靠 `TryParseAdjudicationResult` 正则抽 JSON + 失败换服务重试（`LlmMatchingAssistService.cs`），解析失败计入熔断。
- **方案**：vLLM 原生支持 guided decoding（OpenAI 兼容的 `response_format: json_schema` / `guided_json` extra body），在**推理层强制输出合法 JSON**，verdict/reasonType 还能用 enum 约束，解析失败率直接归零。
- **改动点**：`CreatePromptExecutionSettings`（当前只设了 `Temperature=0`）补 `ResponseFormat`；🟡 需验证 Semantic Kernel OpenAI 连接器对 json_schema 的透传方式（不行就在该处改用带 extra body 的请求）。
- **收益**：消灭无效重试与熔断误触发；约束 verdict 枚举即约束了幻觉空间。工作量：半天。

### A2. Qwen3-Embedding 指令前缀（query 侧 instruct）🟡
- **现状**：`GetSourceEmbeddingText` 直接发原文，无指令前缀。
- **依据**：Qwen3-Embedding 官方建议查询侧加任务指令（"Instruct: 给定验收规格查询，检索语义等价的历史规格\nQuery: ..."），文档侧保持原文，检索精度一般可提升 1~5%（据官方说明，建议实测验证）。
- **关键约束**：改了语料必须隔离缓存——沿用本次 `matching-specification-only` 的做法，新开 usage 或在 ModelName 上加后缀，**不动现有缓存**。
- **验证方式**：用 `tools/MatchingRegressionReport` + 真实导出数据 A/B 对比命中率再决定是否切换。工作量：1 天（含验证）。

### A3. 置信度校准：logprobs 替代 LLM 自报 confidence（P2）🟡
- **现状**：confidence 是 LLM 自报数字，LLM 自报置信度普遍过度自信（经验判断），而 0.5/0.85 两道门槛都依赖它。
- **方案**：vLLM 暴露 logprobs，可用 verdict 首 token 的概率作为校准置信度；先并行记录两种置信度对比分布，再决定是否切换门槛依据。
- **前提**：积累一批带人工最终结论的样本（依赖 C1 标注回路）。

### A4. Few-shot 正反例入模板（todo 既有 P0，待业务样本）
- 做法明确：从真实人工确认记录挑 10~20 对典型正反例，写进 `/config` 的等价裁决模板（零代码）。**需用户提供样本**。
- 注意与 B2 配合：示例属于固定前缀，应放在模板**最前部**（见 B2 原因）。

### A5. 准-1 型号正则真实误报率量化（todo 既有，待真实数据）
- Codex 的 `tools/MatchingRegressionReport` 已具备回放能力，但 fixtures 是精选样本；**需要一份真实规格库导出**跑一遍，统计 `identifier_conflict` 命中分布，确认放宽正则后的误报率可接受。

### A6. 高危行自一致性投票（P2）🟢
- 对 `identifier_conflict` 行（错填物料是最危险错误），LLM 裁决从单次调用改为 n=3 采样投票（temperature>0），2/3 多数才放行。vLLM 连续批处理下 3 次调用成本接近 1 次。
- 实现点：`ApplyLlmEquivalenceAdjudicationAsync` 对 `HasIdentifierConflict` 行循环 3 次取多数。工作量：半天。

### A7. 文本相似度打分从"二值包含"改连续分 🟢
- **现状**：`ComputeProjectScore`/`ComputeSpecificationTextScore` 只有 1.0 / 0.85(0.88) 包含 / 0 三档，排序粒度粗，歧义判定（ScoreGap）受其影响。
- **方案**：改归一化编辑距离（Levenshtein/Jaro-Winkler）连续分；保留包含语义的下限。风险低、改善 Top1/Top2 区分度。工作量：半天 + 基线回放验证。

### A8. RecallTopK 上限 3 → 5（vLLM 后评估）
- `MatchingThresholds.MaxRecallTopK = 3`，Top1 漏选时 AI 重排只有 2 个备选。vLLM 后重排成本下降，可放宽到 5。
- 注意重排 Prompt 长度随候选数增长；与 A1 结构化输出一起做更稳。

---

## B. 速度

### B1. vLLM 上线后的系统参数匹配 ⭐（部署完成即做）
- `LlmParallelism` 默认 4，且代码 `Math.Clamp(config.LlmParallelism, 1, 10)` **上限钉死 10**（`SemanticKernelMatchingService.cs`）；vLLM `--max-num-seqs 16` 时应放宽 clamp 上限到 16 并把默认调到 8~12。
- 一并复核 `LlmRowTimeoutSeconds`（vLLM 单行延迟显著降低，120s 可下调到 60s，加快失败行周转）。

### B2. Prompt 模板段落重排，吃满 vLLM prefix caching ⭐🟢（高杠杆、零风险）
- **现状**：等价裁决模板是"变量在前（源项/候选项），固定规则在后"。vLLM 的 automatic prefix caching 只复用**公共前缀**的 KV——变量放前面导致每行的公共前缀只有第一句，规则段（占模板大头）每次都重新 prefill。
- **方案**：把模板调成"固定段（角色+全部规则+few-shot 示例）在最前，变量段（源项/候选/得分/证据）在最后"。批量裁决时上千行共享同一段长前缀的 KV cache，prefill 成本大幅下降。
- **实现**：只改 `PromptTemplateCatalog` 默认模板内容（旧版进 AdditionalLegacyContents 自动升级，本次已有成熟先例）；vLLM 侧确认 `--enable-prefix-caching`（新版默认开）。工作量：1 小时 + 验证。

### B3. LLM 调用设 MaxTokens 上限 🟢
- **现状**：`CreatePromptExecutionSettings` 只设 `Temperature=0`，无输出上限；裁决/重排只需几百 token 的 JSON，runaway 生成会白占批处理槽位直到行超时。
- **方案**：裁决/重排设 `MaxTokens ≈ 512`。与 A1 结构化输出叠加后输出长度天然受控。工作量：10 分钟。

### B4. Embedding 批与预热
- `SemanticKernelEmbeddingService` HTTP 批大小固定 100，vLLM 下可调大（200~500）减少往返；
- `EmbeddingCacheWarmup.Enabled` 默认 **false**——上 vLLM 后建议开启定时预热，导入后冷启动消失；
- 预热目前只覆盖 `matching` usage，**未覆盖** `matching-specification-only`（本次新增），仅规格模式首跑会慢一次，可在 WarmupAsync 里加一轮。

### B5. 候选向量范数预归一化（既有遗留，小项）
- SIMD 已上；再把候选向量入库/水合时预归一化，相似度退化为纯点积，可再省约 1/3 计算。万条以下感知不大，与两阶段召回（P2）二选一即可。

---

## C. 智能

### C1. 主动学习标注回路 ⭐（长期价值最高的单项）
- 人工确认（接受/拒绝/改选）落库为标注样本：`POST /api/feedback` + 存储表 + 前端确认回调埋点。
- 这是 A3（置信度校准）、C2（案例检索）、C3（QLoRA）的共同前置，建议单独立项先做。

### C2. 案例检索增强裁决（RAG over 历史人工决策）
- 裁决某行时，按 Embedding 检索 3~5 条**历史人工已确认**的相似判例（含当时的接受/拒绝结论），注入 Prompt 作为动态 few-shot。
- 比静态 few-shot 更贴业务、随数据自动进化；依赖 C1 数据。工作量：2~3 天。

### C3. QLoRA 微调（todo 既有 P2，前置 C1 攒 500+ 样本）

### C4. 知识库管理产品化 🟢
- **现状**：单位/品牌词条在 `smart-fill-knowledge.json` 文件里，运营补词条要改文件+重启应用（`SpecCanonicalizer` 构造时一次性加载）。
- **方案**：词条入库 + `/config` 增加管理页 + 规范化器支持热重载（或缓存失效）。把准-3 的"持续运营 SOP"从工程师操作变成业务人员可自助。工作量：2~3 天。

### C5. 决策漏斗看板 🟢
- 聚合各层命中率：原文精确 / 规范化 / 近似规范化 / 确定性自动 / LLM 放行 / LLM 拒绝 / 人工。数据已在 ExecutionHistory 快照里，缺聚合与展示。
- 价值：① 量化每次优化的真实效果；② 监测漂移（如某客户文档格式变化导致精确层命中骤降）。工作量：1~2 天。

### C6. Embedding 升级路径（P2，勿换小模型）
- 方向 A：Qwen3-Embedding-8B（注意 4090 与 14B LLM 共存需 AWQ 量化版，显存预算重排）；
- 方向 B：MRL 维度裁剪（2560→1024）提速召回，精度损失需 A/B。
- ❌ 重申：勿换 bge-m3（降级，见 architecture-improvements.md 已撤销项）。

---

## D. 工程清理（顺手项）

| 项 | 说明 |
|---|---|
| D1 | `BatchMatchResult.MediumConfidenceCount/LowConfidenceCount` 与 `IsMediumConfidence/IsLowConfidence` 是死代码（展示实际走 `GetConfidenceLevel`），删除或重定义 |
| D2 | `LlmParallelism` clamp 上限 10 放宽（同 B1） |
| D3 | todo 文档（vLLM 指南的 Docker 章节 + LM Studio 修正）尚未提交 git |
| D4 | 仓库 `git gc`：提交时提示 too many unreachable loose objects |

---

## 已核实"非问题"，勿重复排查

- 文本管道语料不一致：当前注册 `MinimalTextPreprocessingPipeline`，规则全关，仅空白归一化，对向量无影响；
- `OnTokenValidated` 每请求查库：RBAC 即时吊销（permission_version）所需，按设计如此；
- `EmbeddingCache` 查询索引：已有 `(SpecId, ModelName, Usage)` 唯一索引，无缺失；
- AI 服务/模板的请求级缓存：服务为 Scoped，无陈旧性问题。

---

## 推荐执行顺序

| 顺位 | 项 | 时机 | 工作量 |
|---|---|---|---|
| 1 | B1+B3（并发/超时/MaxTokens 参数匹配） | vLLM 验证通过后立即 | 0.5 天 |
| 2 | B2（模板段落重排吃 prefix caching） | 同上 | 0.5 天 |
| 3 | A1（Structured Outputs） | 同上 | 0.5~1 天 |
| 4 | A5（准-1 真实数据误报率量化） | 拿到真实导出即可 | 0.5 天 |
| 5 | C1（标注回路） | 单独立项 | 3~5 天 |
| 6 | A4（few-shot，依赖业务样本）+ A7（连续相似度分） | 随时 | 各 0.5 天 |
| 7 | C5（决策漏斗看板）、C4（知识库管理页） | 第二梯队 | 各 2~3 天 |
| 8 | A2（Embedding 指令前缀，A/B 验证） | 第二梯队 | 1 天 |
| 9 | A3/A6/C2/C3/C6/B5（P2，多数依赖 C1 数据） | 数据就绪后 | — |
