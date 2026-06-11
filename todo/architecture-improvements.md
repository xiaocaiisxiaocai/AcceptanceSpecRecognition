# 架构改进 TODO

> 创建时间：2026-06-10
> 背景：基于当前智能填充匹配系统（SemanticKernelMatchingService + LlmMatchingAssistService）的架构评估

---

## P0 立即可做（1天内）

- [ ] **换 vLLM 替代 Ollama**
  - 工具：`pip install vllm`
  - 命令：
    ```bash
    python -m vllm.entrypoints.openai.api_server \
      --model Qwen/Qwen2.5-14B-Instruct-AWQ \
      --quantization awq \
      --max-model-len 4096 \
      --max-num-seqs 32 \
      --port 11434
    ```
  - 收益：并发从1提升到32+，批量填充速度数倍提升
  - 迁移成本：仅改服务配置 BaseUrl/端口，业务代码零改动

- [ ] **Prompt 模板加 Few-shot 领域示例**
  - 文件：数据库 PromptTemplate 表（等价裁决场景）
  - 做法：在现有 `BuildEquivalenceAdjudicationPrompt` 模板里追加 10-20 条典型正反例
  - 示例：
    - "DC 12V" 与 "12VDC" → equivalent（format_only）
    - "≥5MΩ" 与 ">5MΩ" → hard_conflict（符号含义不同）
  - 收益：准确率提升约 5%，零代码改动

---

## P1 短期（1-2 周）

- [ ] **主动学习 + 标注回路**
  - 收集用户"人工确认"操作（ManualReview → 接受/拒绝）存为训练样本
  - 实现位置：前端确认回调 → POST /api/feedback → 保存标注记录
  - 目标：积累 500+ 条后触发 QLoRA 微调
  - 训练样本格式：
    ```json
    {"messages": [
      {"role": "user", "content": "判断以下两条验收规格是否等价：\n源：工作电压 DC 12V±10%\n候选：工作电压 12VDC（±10%）"},
      {"role": "assistant", "content": "{\"verdict\": \"equivalent\", \"reasonType\": \"format_only\", \"confidence\": 0.97, \"reason\": \"电压值一致，格式差异\"}"}
    ]}
    ```

- [ ] **~~替换 Embedding 模型为 BAAI/bge-m3~~（已撤销，属错误建议）**
  - ❌ 撤销原因：当前已使用 `qwen3-embedding:4b-q4_K_M`（40亿参数，2560维），
    比 bge-m3（约5.6亿参数，1024维）更强。换 bge-m3 是降级，无意义。
  - 说明：q4_K_M 是量化，压的是磁盘/显存体积，模型能力仍是 4B 档
  - 若 Embedding 真要优化，方向应是"升级到 Qwen3-Embedding-8B"或"用 MRL 裁剪维度提速"，而非换小模型

---

## P1 扩大 LLM 覆盖范围（提升自动填充率）

> 背景：当前三类 Warning 直接转人工，LLM 根本看不到；召回阈值过保守导致语义等价的候选被提前丢弃。

- [x] **改动1：Prompt 加入候选的 Acceptance/Remark 字段（✅ 2026-06-11 已实现）**
  - 实现：`LlmEquivalenceAdjudicationRequest` 已加 `CandidateAcceptance`/`CandidateRemark`，模板新增 `{{candidateAcceptance}}`/`{{candidateRemark}}` 占位符，旧模板经 `AdditionalLegacyContents` 启动自动升级
  - 文件：`LlmMatchingAssistService.cs` → `BuildEquivalenceAdjudicationPrompt`
  - 做法：`LlmEquivalenceAdjudicationRequest` 加 `CandidateAcceptance`、`CandidateRemark` 字段，追加到模板占位符
  - 原因：LLM 看到候选验收标准内容后，能更准确判断语义等价（如"工作电压 12VDC"配合 Acceptance "DC 12V±10%" 即可确认）
  - 收益：裁决准确率直接提升，改动极小

- [x] **改动2：Warning 路径改为"强制 LLM"而非"直接人工"（✅ 2026-06-11 已实现）**
  - 实现：`DetermineDecision` 不再在 LLM 结论之前因 Warning 转人工；Warning/型号冲突行强制进入 LLM 裁决（`ShouldRunLlmEquivalenceAdjudication`）；LLM Equivalent + 置信度达标 → AutoApply（Warning 仍阻断确定性自动通过路径）
  - 注：核对源码发现原状比 todo 描述更糟——LLM 当时已被调用（白烧预算），只是结论被 Warning 门禁丢弃
  - 文件：`SemanticKernelMatchingService.cs`
  - 当前问题：`DetermineDecision`（第1472行）检测到 `unknown_unit_token` / `unknown_brand_token` / `unsupported_format_token` 直接返回 ManualReview，LLM 完全绕过
  - 改法：
    1. `ShouldRunLlmEquivalenceAdjudication` 中 Warning 行标记为"必须进 LLM"
    2. `DetermineDecision` 中有 Warning 时不直接 ManualReview，等 LLM 裁决结果
    3. LLM Equivalent + 置信度 ≥ 阈值 → AutoApply；其余 → ManualReview
  - 典型受益场景：
    - `节拍 5 件/班` vs `节拍 40 件/天`（"件/班"分母不在单位表 → 触发 unknown_unit → 当前转人工，LLM 可结合班制判断是否等价）
    - `品牌要求：ABB` vs `品牌要求：ABB集团`（当前转人工，LLM 能识别同一品牌）
  - 注意：`bar`/`kPa` 等已在单位表，会被规范化层直接判等价，不属于此场景（勿用作示例）

- [x] **改动3：骨架相似候选扩展召回（✅ 2026-06-11 已实现）**
  - 实现：`EvaluateCandidates` 新增 `IsSkeletonRescueCandidate`——Embedding∈[0.50, 召回阈值) 且规格骨架一致时救回候选，标记 `IsSkeletonRescue` 强制进 LLM 裁决
  - 门控：仅规格模式只比骨架；项目+规格模式额外要求项目精确命中（避免通用数值骨架"电压#V"导致召回泛滥）；骨架计算仅在常规召回未命中时才做，限制成本
  - 注：`3000rpm vs 50r/s` 这个具体例子已由 `r/s` 单位词条在规范化层直接判等价（更优）；救援主要覆盖"数值不同但结构相同"被静默丢弃的候选，把它救回视野（带冲突信息）
  - 测试：`BatchMatch_WhenSkeletonEqualButEmbeddingLow_ShouldRescueCandidateIntoView`、`...WhenSkeletonDiffersAndEmbeddingLow_ShouldStillDrop`

- [x] **改动4：LLM 置信度分层（✅ 2026-06-11 部分实现：展示分层）**
  - 实现：`GetConfidenceLevel` 中 LLM 判等价的自动通过，按 `HighConfidenceLlmEquivalenceMinConfidence`(0.85) 拆分——≥0.85 显示 high，[0.5,0.85) 显示 medium（前端已能渲染三级，无需前端改动）
  - 说明：[0.5,0.85) 区间本就已自动填充（门槛是 0.5），本次只补"中置信"展示标注供审核员优先复查，**不改变自动填充行为**
  - 仍未做：把 0.5 硬截断本身改为可配置的三级决策（需产品决定是否进一步收紧/放宽自动填充边界）
    - confidence < 0.5 → ManualReview
  - 收益：提高自动填充率，同时保留可见性

---

## P2 中期（1-2 个月）

- [ ] **QLoRA 微调 Qwen2.5 14B**
  - 前提：标注样本积累到 500-2000 条
  - 工具链：`transformers` + `peft` + `trl` + `bitsandbytes`
  - 4090（24GB）用 QLoRA（4bit量化 + LoRA）显存约 14-16GB，可行
  - 预期效果：等价判断准确率从 ~87% 提升到 ~95%

- [ ] **两阶段 Embedding 召回（粗排+精排）**
  - 粗排：轻量小模型快速取 Top50（毫秒级）
  - 精排：当前的 `qwen3-embedding:4b` 对 Top50 重排取 Top3
  - 注意：精排沿用现有 Qwen3-Embedding-4B，不要换 bge-m3（见 P1 已撤销项）
  - 适用场景：规格库万条以上时收益显著

- [ ] **Embedding 增量更新**
  - 当前：全量预热（每次导入后触发）
  - 改为：新规格入库时增量追加向量缓存
  - 减少冷启动延迟

---

## P0-准确性 源码核对后确认的高收益项（2026-06-10 追加）

> 优先级排序：准确性 > 智能 > 速度 > 其他。
> 以下结论均已逐行核对源码确认，并已剔除子代理分析中的不实结论（见末尾"已排除的伪命题"）。

### 准确性维度（最高优先级）

- [ ] **准-1：型号识别正则只认大写带连字符，漏检导致错填风险（高风险改动，需测试护栏）🔴**
  - 文件：`MatchEvidenceBuilder.cs:16`
  - 现状正则：`@"\b[A-Z]{2,}(?:-[A-Z0-9]+)+\b"`，只匹配「全大写 + 至少一个连字符」
  - 漏检示例：`MK2530`（无连字符）、`6204ZZ`（纯数字字母）、小写型号
  - 危害本质：型号识别用于检测 `identifier_conflict`（line 87-94）。漏掉型号 = 漏掉冲突 = 把"型号不同的两条规格"误判为可自动填充 → **错填物料号**，是验收场景最危险的错误
  - 风险：放宽正则会增加误报（普通词被当型号）。**必须配套测试用例 + 真实历史数据回归验证**
  - 建议：先写一次性离线统计脚本，量化放宽后误报率，再决定改法

- [ ] **准-2：型号"单数兜底冲突"过于激进（与准-1 耦合，需一起评估）🟡**
  - 文件：`MatchEvidenceBuilder.cs:97-116`
  - 现状：源和候选各仅 1 个型号且不相等 → 直接判 `Conflict`
  - 隐患：准-1 放宽正则后，此兜底会与误报叠加放大。两改动必须一起做、一起测

- [ ] **准-3：复合单位分母白名单过窄（运营 SOP，非代码任务）🟢**
  - 文件：`SpecCanonicalizer.cs:797-804` `IsKnownCompoundDenominator` 只认 `s/sec/min/h/hr`
  - 现象：`件/班`、`个/周期`、`次/模` 等节拍单位分母不在白名单 → 报 unknown_unit → 转人工
  - 解法：**外置 JSON 扩展 `piece_rate_*` 量纲即可**（JSON 已有 `件/min`、`件/h` 先例）
  - 建议：把"补充 JSON 单位词条"做成持续运营 SOP，而非一次性代码改动

### 智能维度

- [x] **智-1：型号冲突应作为"强制进 LLM"信号，而非直接拒绝（✅ 2026-06-11 已实现，描述当时已过时）🟡**
  - 核对结论：当时源码已让 identifier_conflict 进 LLM（有测试），真正缺口是 severity="high" 在决策层无任何消费点——LLM 误判 Equivalent（置信度仅需 0.5）即可错填物料号
  - 实现：型号冲突行 LLM Equivalent 需置信度 ≥ `IdentifierConflictEquivalenceMinConfidence`(0.85) 才放行（含语义优先模式）；`RequiresManualReview` 补充 Conflict 关系阻断确定性自动通过
  - 文件：`MatchEvidenceBuilder.cs:88`（severity="high"）+ `SemanticKernelMatchingService.cs` `DetermineDecision`
  - 现状：`identifier_conflict` 走 `RequiresManualReview` 路径直接转人工
  - 问题：`SKF-6204-2Z` vs `SKF 6204 2Z`（空格 vs 连字符）这类纯格式差异本可由 LLM 判等价，却被直接拒绝
  - 改法：与"P1 改动2（Warning 强制进 LLM）"合并设计——型号冲突也纳入 LLM 触发信号

- [x] **智-2：重申 P1 改动1（Prompt 加 Acceptance/Remark）是准确性最高杠杆 ⭐（✅ 2026-06-11 已实现，见改动1）**
  - 30 分钟、纯增益、零风险，应排在所有智能/准确性改动的**最前面**先做

### 推荐实施顺序（避免冒进）

1. **智-2（Prompt 加 Acceptance/Remark）** — 30分钟，纯增益零风险，准确性立即提升
2. **准-1 + 准-2 + 智-1 一起做** — 型号正则放宽 + 兜底重评 + 冲突转 LLM，必须配套回归测试。准确性最大杠杆，但需测试护栏
3. **准-3 单位 JSON 扩展** — 做成运营 SOP，持续维护

> ⚠️ 准-1 风险提示：型号正则放宽前，建议先用真实历史数据做离线误报率统计，再决定改法。可先写一次性统计脚本。

### 已排除的伪命题（子代理分析中的不实结论，勿浪费工时）

- ❌ "缺加速度/密度/湿度等量纲" —— 单位表从外置 JSON（`smart-fill-knowledge.json`）合并加载（`SpecCanonicalizer.cs:997-1052`），补量纲改 JSON 即可，零代码
- ❌ "品牌字典不全" —— 品牌表同样外置可扩展，当前已覆盖约 100 家品牌（内置 ~70 + JSON ~35）
- ❌ "召回下降 30-40%" —— 无数据支撑的臆测，不采信

---

## Ollama 临时优化（换 vLLM 前的过渡）

```bash
# 允许多并发
OLLAMA_NUM_PARALLEL=4 ollama serve
```

---

## 参考

- 当前匹配流水线：精确命中 → 规范化命中 → 近似规范化 → Embedding召回+重排 → LLM灰区裁决
- LLM 裁决限流：`LlmCallBudget` + `LlmCircuitBreaker`（代码在 `SemanticKernelMatchingService.cs`）
- Prompt 模板存储：数据库 `PromptTemplate` 表，可在 `/config/` 页面编辑
