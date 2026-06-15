# 让"同义不同表述"更多靠 AI 自动匹配 — 设计文档

- 日期：2026-06-15
- 范围：智能填充匹配(`SemanticKernelMatchingService` 决策 + 匹配配置 + 智能填充前端默认/展示)
- 状态：设计已与用户对齐,待实现

## 1. Context(为什么做)

领导诉求:让"意思相同、措辞不同"的规格更多靠 AI **自动匹配**,而不是看起来像字符匹配。

本设计**基于在客户30/制程1(117 条真实规格)上的实测**(语义优先 关 vs 开,三条温和改写 + 三条重度改写),结论:

1. **Embedding 语义匹配已可用**:6 条同义改写全部找对了原始规格(温和改写 Emb 0.93~0.96,重度改写 0.69~0.85)。"认出同义"这步 AI 做到了。
2. **语义优先的价值在"召回"**:重度改写中 Emb=0.69 的一条,**关模式下因低于召回门槛(0.9)被直接漏掉(无匹配)**,**开模式(门槛降 0.5)成功召回并找对**。即关模式会"悄悄漏掉"较难的同义表述。
3. **自动填充的真正瓶颈是 LLM 判 `uncertain`**:即便召回到了,本地 LLM 对真正的同义改写**一律判 uncertain(置信 0)**→ 综合分 < 高置信阈值 → **全部转人工**。语义优先不改变这一点(它动召回/硬冲突,不改 LLM 的 verdict)。

目标:针对上面 2、3 两个真实瓶颈,让同义表述既**被找到**、又能**自动填上**,同时把"AI 在做语义判断"在界面上**看得见**。vLLM/cross-encoder 重排**暂停**(无硬件)。

## 2. 设计(三部分)

### 2.1 智能填充默认开启"语义优先模式"（解决"被漏掉"）
- 把前端智能填充默认配置 `defaultMatchConfig.enableLlmSemanticPriority` 改为 `true`(`web/src/api/matching.ts:319`),`llmSemanticRecallThreshold` 维持 `0.5`。
- **后端 `MatchingConfig` 默认仍为 `false`**(避免影响回归基线 `MatchingRegressionReport`、各单测与其它调用方;它们都显式构造配置或依赖默认标准模式)。即"默认开"只作用于智能填充 UI 流程。
- 现有 `MatchConfig.vue` 的「LLM 语义优先」开关保留,用户可关。

### 2.2 新增"高 Embedding 自动通过"路径（解决"召回到了却转人工"）
核心:当 Embedding 足够高、且无任何危险冲突、且 LLM 没判"不同"时,**即使 LLM 说 uncertain 也自动通过**——把强 Embedding 当作足够的 AI 语义证据。

- 新增配置 `MatchingConfig.EmbeddingSemanticAutoApplyThreshold`(double,**后端默认 0 = 关闭**;前端智能填充默认 `0.90`)。取值 `(0,1]` 时启用,值即"高线"。
- 决策(`SemanticKernelMatchingService.DetermineDecision`)**新增分支**,放在"硬冲突→人工"之后、"LLM uncertain→人工"之前:

  ```
  自动通过(AutoApply) 当且仅当:
    EmbeddingSemanticAutoApplyThreshold ∈ (0,1]
    且 candidate.EmbeddingScore >= EmbeddingSemanticAutoApplyThreshold
    且 不歧义(!isAmbiguous)
    且 无硬冲突(!HasHardConflict)              ← 数值/单位/比较符/温度/极性反义/尺寸元组
    且 无型号/料号冲突(!HasIdentifierConflict)  ← 错填物料最危险,排除
    且 无自动通过阻断警告(!HasAutoApplyBlockingWarning) ← 未识别单位/品牌/格式
    且 LLM 裁决 ≠ Different(uncertain / equivalent / 无裁决 均可)
  ```
- 这条路径**不覆盖 `LLM=Different`**(尊重 LLM 明确判异)、**不覆盖硬冲突/型号冲突/未识别警告**(确定性闸门继续兜底)。精度由"高 Emb 线 + 三类闸门 + 不覆盖 different"三重控制。
- **保留真实 LLM verdict(如 uncertain)不覆盖**(透明优先);仅把决策置为 AutoApply,并给 `SelectionSummary` 追加明确说明:"高 Embedding 语义相似(Emb=xx)且无结构化冲突,LLM 未确认,凭语义相似度自动通过"。因此这类行的置信度分级按真实情况(`Score<0.95` 且 LLM≠equivalent → 中/低置信),前端以"AI语义命中"标签 + Emb/裁决展示,既自动填充又如实标注"凭高相似度放行,建议优先复查"。

### 2.3 可见性（满足"看得见 AI 在做语义判断"）
在智能填充**主预览表**(`MatchPreviewTable` 及相关 cell),对每行展示"决策依据"标签,复用已有字段:
- 标签:`精确直达`(exactShortcut)/`AI语义命中`(embeddingTop1/aiRerank 且自动通过)/`需人工确认`(manualReview)/`无匹配`;
- AI 语义命中行,内联展示 `Emb 分%` 与 `AI 裁决结论`(等价/不同/不确定);
- 详情弹窗 `ScoreDetailDialog` 已有完整理由/置信度,不改。

## 3. 改动文件
- `src/AcceptanceSpecSystem.Core/Matching/Models/MatchingModels.cs`:`MatchingConfig` 增 `EmbeddingSemanticAutoApplyThreshold`(默认 0)+ `MatchingThresholds` 增对应常量/取值范围说明。
- `src/AcceptanceSpecSystem.Core/Matching/Services/SemanticKernelMatchingService.cs`:`DetermineDecision` 增"高 Embedding 自动通过"分支 + 合成 LlmEquivalence/SelectionSummary。
- `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`:`MatchConfigDto` 增同名字段并透传。
- `web/src/api/matching.ts`:`MatchConfig` 类型增字段;`defaultMatchConfig` 设 `enableLlmSemanticPriority:true`、`embeddingSemanticAutoApplyThreshold:0.90`。
- `web/src/views/smart-fill/components/MatchConfig.vue`:暴露"高 Embedding 自动通过"阈值(可调/可关)。
- `web/src/views/smart-fill/components/MatchPreviewTable.*`:决策依据标签 + 内联 Emb/裁决。
- 测试:见第 4 节。

## 4. 验证
**后端单测**(`tests/AcceptanceSpecSystem.Core.Tests/EvidenceDrivenSemanticMatchingTests.cs`,默认 0 不影响既有用例):
1. 高 Emb(≥阈值)+ LLM uncertain + 无冲突 + 阈值=0.90 → **AutoApply**(核心新行为);
2. 高 Emb + 硬冲突(数值/极性)→ 仍 ManualReview;
3. 高 Emb + 型号/料号冲突 → 仍 ManualReview;
4. 高 Emb + 未识别单位/品牌警告 → 仍 ManualReview;
5. 高 Emb + LLM=Different → 仍 ManualReview(不覆盖明确判异);
6. Emb < 阈值 → 仍 ManualReview;
7. 阈值=0(默认)→ 行为与现状完全一致(回归保护)。

**回归不破**:`EvidenceDrivenSemanticMatchingTests`、`ReviewRegressionTests`、`MatchingRegressionReport` 全绿(默认 0 → 零行为变化)。

**前端**:`pnpm typecheck`;预览表标签的小用例(可选)。

**真实验证(非 CI)**:对客户30/制程1 重跑 `heavy.docx`(fileId 369)预览,确认重度改写在"默认开 + 阈值0.90"下由 manualReview 变为 **AutoApply**(对照本设计 Context 的实测基线)。

## 5. 非目标 / 风险
- **非目标**:不动确定性规范化层/冲突扫描器;不改 vLLM/重排;不改后端全局默认(只改智能填充 UI 默认)。
- **精度风险**:高 Emb + LLM uncertain 自动通过,可能放过"相似但实不同、且冲突扫描器未覆盖、LLM 又没判 different"的细微差异。控制:高 Emb 线(默认 0.90,可上调)+ 三类闸门 + 不覆盖 different;阈值可调、可关;型号/料号与未识别项一律排除。
- 本地 LLM 非严格确定(同输入偶尔不同 verdict)——本设计正是为减少对其单点依赖。
