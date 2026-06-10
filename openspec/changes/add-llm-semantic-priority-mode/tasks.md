## 1. Specification
- [x] 1.1 确认语义优先模式的开关、触发条件与硬冲突降级规则
- [x] 1.2 确认置信度门槛与召回阈值在语义优先模式下的行为

## 2. Backend
- [x] 2.1 在 `MatchingConfig` 新增 `EnableLlmSemanticPriority`（默认关闭）
- [x] 2.2 在 `MatchingConfig` 新增 `LlmSemanticRecallThreshold`（默认 0.5）
- [x] 2.3 `MatchingConfigResolver` 解析并 Clamp 上述两个字段
- [x] 2.4 `DetermineDecision` 在语义优先模式下让 LLM Equivalent 覆盖硬冲突门禁
- [x] 2.5 语义优先模式下仍校验 `LlmEquivalenceMinConfidence`，置信度不足转人工
- [x] 2.6 语义优先模式下用 `LlmSemanticRecallThreshold` 扩大召回与裁决覆盖面
- [x] 2.7 硬冲突候选在语义优先模式下进入 LLM 裁决而非直接拦截
- [x] 2.8 `MatchingApprovalTokenService` 令牌一致性校验覆盖语义优先相关字段

## 3. Frontend
- [x] 3.1 匹配配置面板暴露语义优先模式开关与召回阈值
- [x] 3.2 API 类型同步新增字段

## 4. Verification
- [x] 4.1 新增语义优先模式覆盖硬冲突的后端测试
- [x] 4.2 新增低 Embedding 候选在语义优先模式下进入 LLM 裁决的测试
- [x] 4.3 运行 `dotnet test AcceptanceSpecSystem.sln -c Debug`
- [x] 4.4 运行前端类型检查与构建
