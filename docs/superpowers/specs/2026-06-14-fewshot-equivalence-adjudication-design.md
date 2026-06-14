# 等价裁决 Prompt 引入 few-shot 示例 — 设计文档

- 日期：2026-06-14
- 范围：仅「智能填充等价裁决」Prompt（`PromptTemplateScene.MatchingEquivalenceAdjudication`）
- 状态：设计已与用户对齐，待实现

## 1. Context（为什么做）

智能填充的灰区行由本地 LLM 做「等价裁决」（equivalent / different / uncertain + reasonType + confidence），
该裁决直接决定「自动填充 vs 转人工」。当前 Prompt（`PromptTemplateCatalog.cs:252-308`
`MatchingEquivalenceAdjudicationDefaultContent`）有详尽**规则**与零散**行内举例**，但**缺结构化 few-shot 示范**
（完整的「输入 → 期望 JSON 输出」演示）。结构化 few-shot 是提升本地小模型判断一致性与输出格式遵从度的关键手段。

本次目标：给等价裁决 Prompt 增加 ~9 个紧凑 few-shot 示例，提升灰区裁决质量，**不依赖运行数据**、低风险、可回归验证。
这是「让 AI 判断更聪明（更 AI 化）」最直接、最便宜的一步，且已列于 `todo/architecture-improvements.md` 的 P0。

## 2. Scope 与非目标

**做**：仅在等价裁决 Prompt 末尾、输出格式说明之前，新增一段【判定示例】。

**明确不做**（用户已确认全部保留）：
- 不删反义词/极性枚举（确定性 `SemanticConflictScanner` 拦已登记的 ~40 对；Prompt 规则兜未登记长尾）。
- 不删输入上下文 JSON（`{{scoreDetailsJson}}`/`{{evidenceSummaryJson}}`/`{{conflictSummaryJson}}`）。
- 不动输出 JSON 协议（`{"verdict","reasonType","reason","confidence"}`，流水线靠它解析）。
- 不动其余三个模板（复核 / 候选重排 / 导入重复复核）。
- 不动确定性层（规范化器、冲突扫描器）与任何决策阈值。

## 3. 设计

### 3.1 模板改造

在现有 `MatchingEquivalenceAdjudicationDefaultContent` 的【品牌等价补充】之后、`仅返回严格 JSON：` 之前，
插入一段【判定示例】。其余文字原样保留。

示例采用**紧凑写法**（控制 token / 延迟）：

```
【判定示例】（仅供参照，按以下风格输出 JSON，不要照抄内容）
1) 源：电机 | 功率 7.5kW
   候选：电机 | 功率 7500W
   {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"7.5kW 换算为 7500W，数值与量纲完全一致","confidence":0.97}
2) 源：伺服电机 | 品牌 Panasonic 型号 MSMF012L1U2M
   候选：伺服电机 | 品牌 松下 型号 MSMF012L1U2M
   {"verdict":"equivalent","reasonType":"equivalent_expression","reason":"Panasonic 与 松下 为同一品牌中英文名，型号一致","confidence":0.95}
3) 源：输出电压：DC 24V（±5%）
   候选：输出电压: DC24V (±5%)
   {"verdict":"equivalent","reasonType":"format_only","reason":"仅全半角/空格/冒号格式差异，数值/单位/容差一致","confidence":0.96}
4) 源：噪音 ≤ 60dB
   候选：噪音 ≥ 60dB
   {"verdict":"different","reasonType":"symbol_conflict","reason":"比较符方向相反（≤ vs ≥），约束含义相反","confidence":0.98}
5) 源：气缸动作 | 到位后气缸上升
   候选：气缸动作 | 到位后气缸下降
   {"verdict":"different","reasonType":"semantic_difference","reason":"动作方向相反（上升 vs 下降）","confidence":0.97}
6) 源：循环时间 ≤ 1秒
   候选：循环时间 ≤ 2秒
   {"verdict":"different","reasonType":"semantic_difference","reason":"同单位下数值不同（1秒 vs 2秒），指标要求不同","confidence":0.97}
7) 源：轴承 SKF-6204
   候选：轴承 SKF-6205
   {"verdict":"different","reasonType":"semantic_difference","reason":"型号尾数不同（6204 vs 6205），为不同物料","confidence":0.96}
8) 源：轴承 SKF-6204-2Z
   候选：轴承 SKF 6204 2Z
   {"verdict":"equivalent","reasonType":"format_only","reason":"仅连字符/空格分隔差异，型号 6204-2Z 一致","confidence":0.95}
9) 源：视觉检测 | 检测精度 高
   候选：视觉检测 | 检测精度 ±0.02mm
   {"verdict":"uncertain","reasonType":"uncertain","reason":"源为定性描述'高'，无法与定量 ±0.02mm 判定等价，需人工确认","confidence":0.3}
```

设计要点：
- 9 例覆盖：单位换算等价、品牌中英文等价、纯格式等价、比较符方向冲突、极性反义、同单位数值不等、
  型号差异、**仅连字符/空格差异的型号等价**（7 与 8 成对，教模型区分「真不同」与「纯格式」这一最危险的混淆）、证据不足。
- 每例的 `verdict`/`reasonType` 组合均满足 `LlmMatchingAssistService.IsCompatibleEquivalenceReasonType` 的兼容约束。
- 明示「仅供参照、不要照抄」，避免模型把示例内容当成待判对象或直接复读。

### 3.2 下发与升级（复用现有机制）

遵循 `PromptTemplateCatalog` 既有的自动升级链（参照 V1/V2）：
- 新建带【判定示例】的内容字符串，设为 `MatchingEquivalenceAdjudicationDefaultContent`（即新的 DefaultContent）。
- 把**当前**的默认内容原文，作为新条目追加进该模板定义的 `AdditionalLegacyContents`。
- 启动时 `LlmMatchingAssistService.LoadTemplateAsync` / `SystemPromptTemplateInitializer` 检测到库中存的是旧默认内容即自动升级；
  用户手工改过的模板不受影响（不在 legacy 列表中）。

无 EF 迁移、无 DTO/接口改动、无前端改动。

## 4. 验证

few-shot 改的是 Prompt 内容。**关键限制**：现有回归基线（`EvidenceDrivenMatchingBaseline.json` +
`MatchingRegressionReport`）用**桩 Embedding、不调用真实 LLM**，只覆盖「给定召回后的裁决/决策逻辑」，
因此**无法验证模型对 few-shot 的实际遵从**。故验证分两层：

**可单测层（CI 可保证）**：
1. **不破回归**：跑 `ReviewRegressionTests`、`EvidenceDrivenSemanticMatchingTests` 及全套测试全绿
   （本改动只动 Prompt 文本，不应影响决策逻辑）。
2. **模板内容/升级断言（新增）**：断言
   `PromptTemplateCatalog.GetByScene(MatchingEquivalenceAdjudication).DefaultContent` 含【判定示例】标记，
   且**当前默认内容**已进入该模板的 `AdditionalLegacyContents`（保证老库自动升级、不丢用户自定义模板）。

**真实遵从层（需真实模型，属"真实数据复测"）**：
3. 上线后用 `tools/SmartFillInsightReport` 观察灰区 `uncertain` 占比是否下降、裁决结论是否更稳；
   必要时据此增删/调整示例。本设计不在 CI 内闭环此项。

## 5. 风险与权衡

- **token/延迟**：9 个紧凑示例约增加数百 token/次，仅作用于灰区行；可接受。若实测偏慢，可降到 5 例（保留 1/4/5/6/9）。
- **过拟合**：示例措辞固定，模型可能偏向示例表述。以「仅供参照、不要照抄」+ 多样类别缓解。
- **本地模型遵从度**：小模型对 few-shot 的吸收因模型而异；若某模型反而变差，回退即删除【判定示例】段即可（低风险、可逆）。

## 6. 改动文件

- `src/AcceptanceSpecSystem.Core/Matching/Services/PromptTemplateCatalog.cs`：
  新增带【判定示例】的 `MatchingEquivalenceAdjudicationDefaultContent`；当前默认内容原文追加进该模板定义的 `AdditionalLegacyContents`。
- 模板内容/升级断言：在现有 Prompt 模板相关测试中就近新增（或新增小测试），覆盖第 4 节第 2 条。
- 无 EF 迁移、无 DTO/接口/前端改动；不改 `EvidenceDrivenMatchingBaseline.json`（桩 LLM 无法验证 few-shot，避免误导性用例）。
