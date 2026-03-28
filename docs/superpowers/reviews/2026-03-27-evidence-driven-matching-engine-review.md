# Code Review — `feat/evidence-driven-matching-engine`

**审查日期**：2026-03-27
**分支**：`feat/evidence-driven-matching-engine`
**审查范围**：本分支相对 `main` 的全量变更

---

## 一、架构与设计

**整体评价：良好**

分支实现了两个核心方向：

1. **Evidence-driven matching engine** — 用证据结构（数值约束、标识符、实体别名）替代原本的纯文本相似度
2. **MatchingKnowledge 配置化** — 原先硬编码的预处理规则（同义词、单位、别名）统一归入可持久化的 `MatchingKnowledge`

架构分层清晰：`IMatchingKnowledgeProvider` → `ConfigurationMatchingKnowledgeProvider` → DB/appsettings 双层回退，合理。

---

## 二、具体问题

### 🔴 高优先级（Blocking）

**1. `SemanticKernelMatchingService.cs` — FinalScore 权重不归一**

```csharp
// ComputeFinalScore（约 line 360）
finalScore = candidate.EmbeddingScore * 0.70 +
             candidate.NumericScore  * 0.10 +
             candidate.KeywordScore  * 0.05 -
             candidate.ConflictPenalty * 0.15;
```

权重正项合计 `0.70 + 0.10 + 0.05 = 0.85`，不构成归一化分布。`Math.Clamp(finalScore, 0, 1)` 掩盖了这个问题。

建议将权重改为正项合计 = 1.0，例如 `0.75 + 0.15 + 0.10`，再单独约束 penalty 扣减上限；或在注释中明确说明这是