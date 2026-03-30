# Change: 为智能匹配新增 LLM 实体判别

## Why
当前品牌/实体识别主要依赖匹配知识配置中的 `EntityAliases`。这意味着像 `Panasonic` / `松下`、`Mitsubishi` / `三菱` 这类未提前配置的中英文别名、简称或组织名差异，无法在运行时被稳定识别，系统只能回退到 Embedding 和通用文本相似度，容易把“品牌一致”和“品牌冲突”混在一起。

用户希望品牌/实体冲突具备“无配置也能识别”的能力，同时对未知品牌、证据不足场景保持保守，不因为 LLM 误判而直接自动采用或直接拒绝。

## What Changes
- 在匹配引擎中新增运行时实体候选提取与轻量归一化能力，用于在无配置场景下抽取品牌/实体候选。
- 在多阶段重排链路中新增可选的 LLM 实体判别阶段，只回答“同一实体 / 别名同一 / 明确冲突 / 无法判断”。
- 新增保守阈值与决策映射规则：高置信实体冲突可以触发拒绝，低置信或未知仅降级为人工确认。
- 复用现有 `issues` 输出，为实体同一、实体冲突、实体未知提供稳定问题编码和用户说明。
- 在智能填充配置中新增“LLM 实体判别”相关开关和阈值，允许用户按批次开启或关闭。

## Impact
- Affected specs: `matching-engine`, `api`, `user-interface`
- Related active changes: `add-match-issue-reporting`, `add-multistage-matching-rerank`
- Affected code:
  - `src/AcceptanceSpecSystem.Core/Matching/*`
  - `src/AcceptanceSpecSystem.Core/AI/SemanticKernel/*`
  - `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
  - `src/AcceptanceSpecSystem.Api/Services/MatchingWorkflowService.cs`
  - `web/src/api/matching.ts`
  - `web/src/views/smart-fill/*`
  - `tests/AcceptanceSpecSystem.Core.Tests/*`
  - `tests/AcceptanceSpecSystem.Api.Tests/*`
