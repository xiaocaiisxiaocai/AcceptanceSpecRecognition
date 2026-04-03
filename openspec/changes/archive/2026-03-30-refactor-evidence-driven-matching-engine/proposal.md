# Change: 重构为证据驱动的强约束匹配引擎

## Why

当前匹配链路以 Embedding 相似度为主，虽能支持多语义表达召回，但在验收规格场景下存在明显风险：

- `220V` 与 `22V`、`<0.5cm` 与 `=0.7cm` 等关键数值冲突可能因语义接近而被打出过高分数
- 型号、料号、方向词、布尔条件、品牌/单位实体等关键字段缺乏统一的硬冲突裁决
- 现有可切换的 `SingleStage / MultiStage` 设计会继续保留“Embedding 直接决定最终高置信结果”的路径
- 品牌别名、单位换算、冲突词对、自动采用门槛与 LLM 复核策略若散落在代码或 prompt 中，后续维护和审计成本都不可接受

本次变更将把匹配链路升级为“语义召回 + 结构化证据 + 硬冲突门禁 + 歧义复核”的统一多阶段引擎，优先保证关键字段准确性，同时保留对多语义表达的召回能力。

本变更替代 `add-multistage-matching-rerank` 中“兼容保留单阶段策略”的演进方向。

## What Changes

- 移除 `SingleStage` 策略与前后端策略切换入口，统一使用证据驱动的多阶段匹配流程
- 将 Embedding 明确限定为第一阶段召回能力，不再作为最终裁决或高置信判定的唯一依据
- 为候选匹配引入统一的 `MatchEvidence` 证据模型，覆盖数值约束、型号/料号、品牌/单位实体、方向词、布尔条件与文本信号
- 引入硬冲突门禁，对关键数值、型号/料号、方向词、品牌/单位明确冲突等场景直接禁止高置信自动采用
- 引入配置化的匹配策略、别名字典、单位换算、冲突词对与 LLM 复核模板，禁止将业务知识硬编码在代码细节中
- 将 LLM 定位为高歧义样本的复核器而非主匹配器；进入复核流程但失败或超时的样本一律转人工确认
- 更新智能填充预览与详情展示，突出证据、冲突、歧义和最终决策原因，而不再仅展示单一综合分

## Impact

- Affected specs:
  - `matching-engine`
  - `user-interface`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/*`
  - `src/AcceptanceSpecSystem.Core/TextProcessing/*`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `web/src/api/matching.ts`
  - `web/src/views/smart-fill/*`
  - `tests/AcceptanceSpecSystem.Core.Tests/*`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
- Breaking changes:
  - 不再支持 `SingleStage` 匹配策略
  - 匹配结果结构将新增证据、冲突、歧义与复核状态字段
  - 智能填充界面将移除“单阶段/多阶段”策略切换配置
