## Context

智能结构识别当前已经保存客户级文档结构模板。模板中包含表头、列映射、表头行数、数据起始行、仅规格模式和使用次数。这些数据已经足够作为 LLM 结构裁决的参考案例。

当前 LLM 结构裁决请求只包含：

- 当前表格摘要 JSON
- 规则识别候选 JSON

因此 LLM 在客户特定叫法、相似表头结构和历史确认习惯上缺少上下文。

## Goals

- 复用 `DocumentTemplate` 作为历史案例来源。
- 只在 LLM 结构裁决阶段注入历史案例。
- 不改变高置信模板命中和规则自动采用路径。
- 不新增数据库表。
- 不改变智能结构识别 API 响应契约。

## Non-Goals

- 不做 Embedding 相似检索。
- 不做跨客户模板共享。
- 不做主动纠错统计。
- 不做前端展示。

## Decisions

### Decision 1: 复用 DocumentTemplate 而不是新增案例表

`DocumentTemplate` 已经保存用户确认后的结构信息，且按客户隔离。第一版直接复用它，能减少迁移和维护成本。

备选方案是新增 `successful_structure_cases` 表。该方案能保存更细的快照，但会引入新迁移、重复数据和额外管理逻辑，不适合作为第一版。

### Decision 2: 采用确定性相似度，不引入 Embedding

第一版使用现有表头相似度思路：同客户模板中，优先选择列数一致、表头编辑距离相近、使用次数高的模板。这样可以在没有 Embedding 服务时稳定运行。

Embedding 可作为后续增强，但不应成为智能结构识别的基础依赖。

### Decision 3: 历史案例只用于 LLM 裁决，不直接自动采用

模板精确命中仍然走现有模板路径。历史案例 Few-shot 只作为 LLM 输入上下文，用于帮助 LLM 在规则失败时判断。

这样能避免“相似但不同”的历史模板直接覆盖当前文档。

## Data Shape

建议新增 Core 模型：

```csharp
public sealed class DocumentStructureReferenceCase
{
    public string TemplateName { get; init; } = string.Empty;
    public IReadOnlyList<string> Headers { get; init; } = [];
    public DocumentStructureCandidate Mapping { get; init; } = new();
    public int UsageCount { get; init; }
    public double Similarity { get; init; }
}
```

`LlmDocumentStructureAdjudicationRequest` 增加：

```csharp
public IReadOnlyList<DocumentStructureReferenceCase> ReferenceCases { get; init; } = [];
```

Prompt 渲染时序列化为 `referenceCasesJson`。

## Risks / Trade-offs

- 风险：历史模板与当前表格相似但语义不同，误导 LLM。
  - 缓解：只传 Top 3；要求 LLM 不得直接复制案例，必须以当前表格为准。
- 风险：Prompt 变长导致 LLM 成本升高。
  - 缓解：仅传表头和映射，不传完整数据行；只在 LLM 裁决阶段传入。
- 风险：旧 Prompt 模板缺少新占位符。
  - 缓解：模板渲染时新占位符为空数组也合法；系统默认模板更新后可恢复默认。

## Testing

- Core 测试：Prompt 渲染包含参考案例 JSON。
- Application/API 测试：规则识别需要 LLM 裁决时，请求中包含同客户历史模板案例。
- 回归测试：无客户、无模板、模板不相似时仍传空案例且不失败。
- 回归测试：高置信模板命中时不进入 LLM 裁决。
