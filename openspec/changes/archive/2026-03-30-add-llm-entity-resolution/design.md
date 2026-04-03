## Context
当前匹配链路已经具备 Embedding 召回、数值/型号/冲突词硬裁决，以及结构化问题项输出能力。但实体识别仍然以知识配置命中为主，无法覆盖未配置的英文品牌、中文别名、组织后缀差异等运行时场景。

如果直接让 LLM 接管主匹配，会破坏当前“硬规则优先”的边界；如果完全不引入 LLM，又无法在无配置场景下可靠区分 `Panasonic` / `松下` 与 `Panasonic` / `Mitsubishi`。因此需要一个独立、窄职责、可保守降级的运行时实体判别阶段。

## Goals / Non-Goals
- Goals:
  - 在无配置场景下提取并归一化实体候选，尽量减少对 `EntityAliases` 的硬依赖。
  - 让 LLM 只判别实体关系，不直接决定整体匹配结果。
  - 对未知品牌、证据不足场景输出 `unknown`，并降级为人工确认，而不是硬判冲突。
  - 继续复用现有 `issues` 结构和预览/详情展示链路。
- Non-Goals:
  - 不自动把运行时实体判别结果写回匹配知识配置。
  - 不让 LLM 推翻数值、型号、冲突词等硬规则裁决。
  - 不在本轮扩展到供应商数据库、工商实体库或联网查询。

## Decisions
- Decision: 新增“实体候选提取”与“LLM 实体判别”两个阶段
  - Why:
    - 轻量提取/归一化适合做确定性预处理，成本低且稳定。
    - LLM 只处理“这两个候选实体是什么关系”这个窄问题，更容易约束输出和控制风险。
  - Alternatives considered:
    - 继续只靠 `EntityAliases`：维护成本高，无法满足无配置识别。
    - 让 LLM 直接输出最终匹配结论：风险过高，会和现有硬规则职责冲突。

- Decision: LLM 只允许输出固定关系枚举和置信度
  - 枚举值：`same`、`alias_same`、`conflict`、`unknown`
  - Why:
    - 避免自由文本污染决策链路。
    - 方便把结果映射成 `EntityEvidence`、`issues` 和最终 `decision`。

- Decision: 只对 TopM 候选触发 LLM 实体判别
  - 默认建议 `TopM = 3`
  - Why:
    - 控制延迟与成本。
    - 当前多阶段匹配已经先做过 Embedding 召回和硬证据重排，没有必要对全量候选调用 LLM。

- Decision: 未知实体或低置信冲突必须保守降级
  - 映射建议：
    - `conflict` 且 `confidence >= 0.90` → 视为高置信实体冲突，可触发 `reject`
    - `conflict` 且 `0.70 <= confidence < 0.90` → 输出冲突问题，但仅降级为 `manualReview`
    - `same` / `alias_same` 且 `confidence >= 0.85` → 作为正向实体证据
    - `unknown` 或低于阈值 → 输出 `entity_unknown`，不直接拒绝
  - Why:
    - 满足“未知品牌/未知实体除外”的业务要求。
    - 把误判风险限制在人工确认，而不是直接影响自动采用结果。

- Decision: 不直接修改 `MatchEvidenceBuilder`
  - Why:
    - 当前 `MatchEvidenceBuilder` 是同步规则组件；LLM 调用是异步行为。
    - 更合适的做法是在 `SemanticKernelMatchingService` 的多阶段重排中增加异步实体判别步骤，再把结果写回 `Evidence` 和 `Issues`。

## Risks / Trade-offs
- 风险: LLM 调用会增加预览延迟。
  - Mitigation: 只对 TopM 候选启用；允许在配置中关闭；复用已有 LLM 服务选择与超时策略。

- 风险: 未知品牌可能被模型误判为冲突。
  - Mitigation: 采用双阈值策略；低于高置信阈值时不直接拒绝，而是输出 `entity_unknown` 或 `manualReview`。

- 风险: 运行时判别结果与知识配置可能冲突。
  - Mitigation: 配置命中优先于 LLM；当配置已明确给出同一实体时，不再触发 LLM 判别。

## Migration Plan
1. 新增实体候选提取与关系判别模型，定义固定 JSON 协议。
2. 在匹配配置中新增 `useLlmEntityResolution`、TopM 和阈值参数。
3. 在 `SemanticKernelMatchingService` 中集成实体判别阶段，并将结果映射到 `Evidence` / `Issues`。
4. 在 API 和前端透传配置与问题提示。
5. 补充 `Panasonic/松下`、`Panasonic/Mitsubishi`、`ABB/abb`、未知品牌等回归测试。

## Open Questions
- 是否需要在本轮为实体判别单独记录诊断日志，便于后续调参。
- 是否需要把“实体候选提取结果”暴露给前端详情弹窗用于调试。
