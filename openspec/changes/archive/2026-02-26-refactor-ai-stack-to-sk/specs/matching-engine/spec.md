## RENAMED Requirements
- FROM: ### Requirement: 文本相似度匹配
- TO: ### Requirement: Embedding 向量匹配（Semantic Kernel）

## MODIFIED Requirements

### Requirement: Embedding 向量匹配（Semantic Kernel）
系统 MUST 使用 Semantic Kernel 的 Embedding 连接器对【项目+规格】组合文本生成向量并计算相似度，作为匹配主链路。

#### Scenario: SK Embedding 相似度计算
- **GIVEN** 用户输入项目与规格
- **WHEN** 系统执行匹配
- **THEN** 系统通过 SK Embedding 生成向量并计算相似度得分

#### Scenario: 离线优先的 Embedding 选择
- **GIVEN** 同时配置本地与在线 Embedding 服务
- **WHEN** 本地服务可用
- **THEN** 系统优先使用本地 Embedding
- **AND** 本地不可用时尝试在线 Embedding
- **AND** 若均不可用则返回“不匹配原因=Embedding 服务不可用”

---

### Requirement: 候选结果排序与Top-N
系统 MUST 仅返回最佳匹配结果（Top‑1），不返回候选列表与 Top‑K 配置。

#### Scenario: 仅返回最佳匹配
- **GIVEN** 匹配结果包含多条候选
- **WHEN** 系统返回匹配结果
- **THEN** 系统仅返回得分最高的1条作为最佳匹配
- **AND** 不返回候选列表

---

### Requirement: 阈值过滤
系统 MUST 对 Embedding 相似度应用阈值，低于阈值视为无匹配。

#### Scenario: 低于阈值的无匹配
- **GIVEN** 用户设置阈值为0.75
- **AND** 最佳匹配得分为0.62
- **WHEN** 系统执行匹配
- **THEN** 系统返回空匹配
- **AND** 返回“不匹配原因=最佳得分低于阈值”

---

## REMOVED Requirements

### Requirement: 匹配结果包含算法得分明细
**Reason**: 传统相似度算法不再作为主链路，评分统一由 Embedding/LLM 输出。
**Migration**: 前端展示改为 Embedding 得分与 LLM 复核评分/理由。

---

## ADDED Requirements

### Requirement: LLM 复核评分/理由（Semantic Kernel）
系统 SHALL 使用 Semantic Kernel 的 LLM 连接器对最佳匹配进行复核评分与理由生成，并通过 SSE 流式返回。

#### Scenario: LLM 复核流式输出
- **GIVEN** 用户启用 LLM 复核且存在最佳匹配
- **WHEN** 系统执行匹配预览并启动 SSE
- **THEN** 系统以流式事件返回 LLM 复核评分与理由的增量内容

#### Scenario: LLM 复核失败不降级
- **GIVEN** 用户启用 LLM 复核且所有 LLM 服务均不可用
- **WHEN** 系统执行匹配预览
- **THEN** 系统返回“不匹配原因=LLM 复核失败”
- **AND** 不降级到非 LLM/Embedding 结果

---

### Requirement: LLM 生成验收建议（Semantic Kernel）
系统 SHALL 在低置信度或无匹配时使用 Semantic Kernel 的 LLM 连接器生成验收/备注建议，并通过 SSE 流式返回。

#### Scenario: 低置信度生成建议
- **GIVEN** 用户启用 LLM 生成且最佳匹配置信度为低
- **WHEN** 系统执行匹配预览并启动 SSE
- **THEN** 系统以流式事件返回 LLM 生成的验收/备注建议

#### Scenario: 无匹配生成建议
- **GIVEN** 用户启用 LLM 生成且该行无匹配
- **WHEN** 系统执行匹配预览并启动 SSE
- **THEN** 系统以流式事件返回 LLM 生成的验收/备注建议

---

### Requirement: 不匹配原因返回（含 AI 失败原因）
系统 SHALL 在无匹配时返回明确原因，至少包含：范围内无候选数据、Embedding 服务不可用、最佳得分低于阈值、LLM 复核失败。

#### Scenario: 范围内无候选数据
- **GIVEN** 用户选择的范围内无候选数据
- **WHEN** 系统执行匹配
- **THEN** 系统返回“不匹配原因=范围内无候选数据”

#### Scenario: Embedding 服务不可用
- **GIVEN** 未配置可用的 Embedding 服务或服务不可达
- **WHEN** 系统执行匹配
- **THEN** 系统返回“不匹配原因=Embedding 服务不可用”

#### Scenario: LLM 复核失败
- **GIVEN** 已获得最佳匹配但 LLM 复核服务失败
- **WHEN** 系统执行匹配预览
- **THEN** 系统返回“不匹配原因=LLM 复核失败”
