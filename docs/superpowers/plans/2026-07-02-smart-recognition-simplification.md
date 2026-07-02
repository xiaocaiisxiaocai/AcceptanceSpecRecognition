# 智能结构识别·流程极简化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 上传文档后自动识别全部结构配置（表/表头/数据范围/四列映射），高置信直达、低置信行内确认卡、确认自动沉淀模板与字典，数据导入与智能填充共用。

**Architecture:** 四层识别流水线（L0 客户模板 → L1 规则字典 → L2 Embedding 锚点 → L3 LLM 裁决）落在 `Core/Documents/Intelligence`；编排/模板/学习在 `Api/Services/SmartConfigurationAppService`；前端两条链路收敛为「上传 → 结果页」两步流。归档分支 `origin/feat/smart-auto-configuration` 的后端骨架捡回复用，前端改动不捡。

**Tech Stack:** .NET 8 / EF Core (MySQL, 测试 SQLite) / Semantic Kernel（复用 `IEmbeddingService`、LLM 服务族 DI 模式）/ Vue 3 + Element Plus。

**设计文档:** `docs/superpowers/specs/2026-07-02-smart-recognition-simplification-design.md`

**工作分支:** `feat/smart-recognition-simplification`（已存在，含设计文档提交）

---

## 全局约束（每个任务都要遵守）

- 架构边界测试是硬门禁：Core 不引用上层；`Api/Services` 服务必须 `public interface IXxxAppService` + `public sealed class XxxAppService : IXxxAppService`，`AddScoped<IXxx, Xxx>()` 注册；控制器注入接口、透传 `CancellationToken`；单文件 < 500 行。
- Schema 变更只走 EF Core 迁移：`dotnet ef migrations add <Name> -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api`。
- 提交信息 `类型: 中文描述`。
- 每个任务完成后跑 `dotnet test AcceptanceSpecSystem.sln -c Debug`（前端任务另加 `cd web && pnpm typecheck`），全绿才提交。

---

## Phase A：捡回归档骨架 + 数据层

### Task 1: 捡回归档分支后端骨架

**Files:**
- Create（从归档分支检出）:
  - `src/AcceptanceSpecSystem.Core/Documents/Intelligence/`（整个目录：`DocumentIntelligenceService.cs`、`IDocumentIntelligenceService.cs`、`Models/*.cs`、`Strategies/*.cs`）
  - `src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/IDocumentTemplateRepository.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/DocumentTemplateRepository.cs`
  - `src/AcceptanceSpecSystem.Api/Services/DocumentTemplateAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Services/SmartConfigurationAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs`

- [ ] **Step 1: 检出归档文件（不检出归档迁移，迁移本地重新生成）**

```bash
git checkout origin/feat/smart-auto-configuration -- \
  src/AcceptanceSpecSystem.Core/Documents/Intelligence \
  src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs \
  src/AcceptanceSpecSystem.Data/Repositories/IDocumentTemplateRepository.cs \
  src/AcceptanceSpecSystem.Data/Repositories/DocumentTemplateRepository.cs \
  src/AcceptanceSpecSystem.Api/Services/DocumentTemplateAppService.cs \
  src/AcceptanceSpecSystem.Api/Services/SmartConfigurationAppService.cs \
  src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs
```

- [ ] **Step 2: 补 DbContext 与 DI 注册**

对照归档分支同名文件，把缺的注册行抄回来（归档分支这几处已写好，直接对照）：

```bash
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs | grep -n "DocumentTemplate"
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Application/DependencyInjection/ServiceCollectionExtensions.cs | grep -n -B2 -A2 "DocumentTemplate\|Intelligence"
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Api/Program.cs | grep -n -B2 -A2 "SmartConfig\|DocumentTemplate\|Intelligence"
```

按 grep 结果把 `DbSet<DocumentTemplate>`、实体配置、`IDocumentTemplateRepository`/`IDocumentIntelligenceService`/`IRuleBasedMappingStrategy`/`ISmartConfigurationAppService` 等注册加到当前分支对应文件。

- [ ] **Step 3: 本地重新生成迁移**

```bash
dotnet ef migrations add AddDocumentTemplate -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
```

Expected: 生成的 Up() 只含 CreateTable "DocumentTemplates"。

- [ ] **Step 4: 编译 + 全量测试**

```bash
dotnet build AcceptanceSpecSystem.sln -c Debug && dotnet test AcceptanceSpecSystem.sln -c Debug
```

Expected: 全绿（归档代码在同一基线上开发过，预期干净落地；若架构边界测试报 `SmartConfigurationAppService` 未接口化等问题，按报错修正后再跑）。

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: 捡回归档分支智能识别后端骨架"
```

---

### Task 2: DocumentTemplate 实体扩展（支撑完整结构 + 使用统计 + 仅规格）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/DocumentTemplateRepositoryTests.cs`（新建）

- [ ] **Step 1: 写失败测试（仓储可按 客户+指纹 命中，新字段可存取）**

```csharp
using AcceptanceSpecSystem.Data.Entities;
using FluentAssertions;

namespace AcceptanceSpecSystem.Data.Tests;

public class DocumentTemplateRepositoryTests : RepositoryTestBase // 沿用本项目现有测试基类；若类名不同，参照同目录其他 *RepositoryTests 的基类写法
{
    [Fact]
    public async Task Upsert_ThenQueryByCustomerAndFingerprint_ShouldRoundTripNewFields()
    {
        var template = new DocumentTemplate
        {
            CustomerId = 1,
            TemplateName = "T1",
            HeadersFingerprint = "fp-001",
            HeadersJson = "[\"规格\",\"验收\"]",
            ProjectColumnIndex = null,          // 本任务改为可空
            SpecificationColumnIndex = 0,
            AcceptanceColumnIndex = 1,
            RemarkColumnIndex = null,
            HeaderRowIndex = 0,
            HeaderRowCount = 1,
            DataStartRowIndex = 1,              // 新增
            DataEndRowIndex = null,             // 新增
            IsSpecificationOnly = true,         // 新增
            UseCount = 1,                       // 新增
            LastUsedAt = DateTime.UtcNow        // 新增
        };
        DbContext.Set<DocumentTemplate>().Add(template);
        await DbContext.SaveChangesAsync();

        var found = DbContext.Set<DocumentTemplate>()
            .Single(t => t.CustomerId == 1 && t.HeadersFingerprint == "fp-001");
        found.IsSpecificationOnly.Should().BeTrue();
        found.ProjectColumnIndex.Should().BeNull();
        found.DataStartRowIndex.Should().Be(1);
        found.UseCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: 跑测试确认编译失败**（新属性不存在）

```bash
dotnet test tests/AcceptanceSpecSystem.Data.Tests -c Debug --filter "FullyQualifiedName~DocumentTemplateRepositoryTests"
```

- [ ] **Step 3: 改实体**——`ProjectColumnIndex` 由 `int` 改 `int?`；`AcceptanceColumnIndex` 改 `int?`；新增：

```csharp
    /// <summary>数据起始行索引</summary>
    public int DataStartRowIndex { get; set; } = 1;

    /// <summary>数据结束行索引（null=至表尾）</summary>
    public int? DataEndRowIndex { get; set; }

    /// <summary>是否仅规格模式（无项目列）</summary>
    public bool IsSpecificationOnly { get; set; }

    /// <summary>命中/确认次数</summary>
    public int UseCount { get; set; }

    /// <summary>最近使用时间</summary>
    public DateTime? LastUsedAt { get; set; }
```

- [ ] **Step 4: 生成迁移 + 全量测试通过**

```bash
dotnet ef migrations add ExtendDocumentTemplateForStructure -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet test AcceptanceSpecSystem.sln -c Debug
```

- [ ] **Step 5: Commit** `git commit -am "feat: 扩展文档模板实体支撑完整结构与仅规格模式"`

---

### Task 3: ColumnMappingRule 扩展（学习来源 + 客户域）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Entities/ColumnMappingRule.cs`
- Test: `tests/AcceptanceSpecSystem.Data.Tests/ColumnMappingRuleLearnedFieldsTests.cs`（新建，写法同 Task 2 Step 1，断言 `Source`/`CustomerId` 往返存取）

- [ ] **Step 1: 写失败测试**（存一条 `Source=Learned, CustomerId=1` 的规则并读回）
- [ ] **Step 2: 跑测试确认编译失败**
- [ ] **Step 3: 改实体**——新增枚举与两列：

```csharp
/// <summary>规则来源。</summary>
public enum ColumnMappingRuleSource
{
    Builtin = 1,   // 内置默认（代码种子）
    Manual = 2,    // 配置页人工添加（存量数据默认值）
    Learned = 3    // 用户确认卡修正自动学习
}
```

`ColumnMappingRule` 增加：

```csharp
    /// <summary>规则来源</summary>
    public ColumnMappingRuleSource Source { get; set; } = ColumnMappingRuleSource.Manual;

    /// <summary>关联客户（null=全局规则）</summary>
    public int? CustomerId { get; set; }
```

- [ ] **Step 4: 迁移 + 全量测试**

```bash
dotnet ef migrations add AddColumnMappingRuleLearning -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet test AcceptanceSpecSystem.sln -c Debug
```

- [ ] **Step 5: Commit** `git commit -am "feat: 列映射规则支持学习来源与客户域"`

---

## Phase B：识别引擎四层（Core，纯单测）

### Task 4: 结构指纹

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/StructureFingerprint.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureFingerprintTests.cs`

- [ ] **Step 1: 写失败测试**

```csharp
using AcceptanceSpecSystem.Core.Documents.Intelligence;
using FluentAssertions;

namespace AcceptanceSpecSystem.Core.Tests.Documents.Intelligence;

public class StructureFingerprintTests
{
    [Fact]
    public void SameHeaders_DifferentWhitespaceAndCase_ShouldProduceSameFingerprint()
    {
        var a = StructureFingerprint.Compute(new[] { "检验 项目", "规格", "OK/NG" }, 3);
        var b = StructureFingerprint.Compute(new[] { "检验项目", " 规格 ", "ok/ng" }, 3);
        a.Should().Be(b);
    }

    [Fact]
    public void DifferentColumnCount_ShouldProduceDifferentFingerprint()
    {
        var a = StructureFingerprint.Compute(new[] { "项目", "规格" }, 2);
        var b = StructureFingerprint.Compute(new[] { "项目", "规格" }, 3);
        a.Should().NotBe(b);
    }
}
```

- [ ] **Step 2: 跑测试失败** `dotnet test tests/AcceptanceSpecSystem.Core.Tests -c Debug --filter "FullyQualifiedName~StructureFingerprintTests"`
- [ ] **Step 3: 实现**

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>文档表格结构指纹：归一化表头序列 + 列数 的稳定哈希，用于客户模板命中。</summary>
public static class StructureFingerprint
{
    public static string Compute(IReadOnlyList<string> headers, int columnCount)
    {
        var normalized = headers.Select(h =>
            Regex.Replace(h ?? string.Empty, @"\s+", string.Empty).ToLowerInvariant());
        var payload = $"{columnCount}|{string.Join("|", normalized)}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)[..32];
    }
}
```

- [ ] **Step 4: 测试通过后 Commit** `git commit -am "feat: 新增文档结构指纹计算"`

---

### Task 5: L1 字典融合（规则策略接收外部词条）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/IRuleBasedMappingStrategy.cs`、`RuleBasedMappingStrategy.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/RuleBasedMappingStrategyTests.cs`（新建）

**接口变更**：`IdentifyAsync` 增加可选参数，外部词条与内置 `DefaultSynonyms` 合并、**外部优先**：

```csharp
Task<ColumnMappingResult> IdentifyAsync(
    IReadOnlyList<string> headers,
    IReadOnlyList<IReadOnlyList<string>> sampleRows,
    IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms = null,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: 写失败测试**——内置词典认不出的表头“管控要点”，通过 extraSynonyms 传入后应识别为 Project 列且来源依据含该词（构造 `ITextPreprocessingPipeline` 用现有 Core.Tests 中已存在的替身/真实实现，参照同项目其他用到该管道的测试）。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现**——`IdentifyColumn` 匹配循环前把 extraSynonyms 对应类型的词并到候选词首（`extra.Concat(builtin)`），命中即返回。
- [ ] **Step 4: 全量 Core 测试通过，Commit** `git commit -am "feat: 规则映射策略支持外部词典融合"`

---

### Task 6: L2 Embedding 锚点列匹配器

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/EmbeddingColumnMatcher.cs`、`IEmbeddingColumnMatcher.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/EmbeddingColumnMatcherTests.cs`

**接口与行为**：

```csharp
using AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Strategies;

/// <summary>L2：未决表头用 Embedding 与四类语义锚点比对。</summary>
public interface IEmbeddingColumnMatcher
{
    /// <returns>每个输入表头的 (类型, 置信度, 依据)；不达标返回 ColumnType.Unknown。</returns>
    Task<IReadOnlyList<(ColumnType Type, double Confidence, string Reason)>> MatchAsync(
        IReadOnlyList<string> unresolvedHeaders,
        CancellationToken cancellationToken = default);
}
```

实现要点（完整逻辑，供直接照写）：
- 依赖 `IEmbeddingService`（`Core/Matching/Interfaces/IEmbeddingService.cs`，已注册 DI）。
- 锚点种子（常量）：Project=`["检验项目","测试内容","管控项目","检查要点"]`；Specification=`["规格要求","技术标准","判定基准","参数指标"]`；Acceptance=`["验收结果","判定结果","实测值","合格判定"]`；Remark=`["备注说明","补充说明","附注"]`。
- 一次 `GenerateEmbeddingsAsync(种子全集 + 未决表头)` 批量取向量；每个表头对每类取「与该类各种子余弦的最大值」为类得分。
- 采纳条件：最高类得分 ≥ **0.80** 且比次高类高 ≥ **0.05**；置信度 = 最高类得分；否则 Unknown。
- 余弦函数实现为类内私有静态方法（两 float[] 点积/模长，长度不一致抛 `ArgumentException`）。

- [ ] **Step 1: 写失败测试**——用假 `IEmbeddingService`（测试内实现接口：固定词→固定向量的字典，例如 Project 种子与“管控要点”返回同向向量，Specification 种子返回正交向量），断言：①“管控要点”→ Project 且置信度≥0.8；②与两类锚点等距的词 → Unknown（歧义拦截）。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 按上述要点实现**
- [ ] **Step 4: 测试通过，Commit** `git commit -am "feat: 新增 Embedding 锚点列匹配器"`

---

### Task 7: L3 LLM 结构裁决服务

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/ILlmStructureRecognitionService.cs`、`LlmStructureRecognitionService.cs`、`Models/LlmStructureResult.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/LlmStructureParseTests.cs`

**模型与接口**：

```csharp
namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

/// <summary>LLM 结构裁决输出。</summary>
public sealed class LlmStructureResult
{
    public int HeaderRowIndex { get; init; }
    public int HeaderRowCount { get; init; } = 1;
    public int DataStartRowIndex { get; init; }
    public int? DataEndRowIndex { get; init; }
    public int? ProjectColumn { get; init; }
    public int? SpecificationColumn { get; init; }
    public int? AcceptanceColumn { get; init; }
    public int? RemarkColumn { get; init; }
    public bool IsSpecificationOnly { get; init; }
    public double Confidence { get; init; }
    public string Reasoning { get; init; } = string.Empty;
}
```

```csharp
public interface ILlmStructureRecognitionService
{
    Task<LlmStructureResult?> RecognizeAsync(
        IReadOnlyList<IReadOnlyList<string>> topRows,   // 表格前 N 行原样（含疑似表头）
        CancellationToken cancellationToken = default);

    bool TryParseResult(string raw, out LlmStructureResult result);
}
```

实现方式：**镜像现有 `ILlmEquivalenceAdjudicationService` 实现类的全部管线写法**（同目录 `Core/Matching/Services/` 下找到其实现文件：chat 服务获取、超时、重试一次、JSON 容错剥离 ```json 围栏）；Prompt 模板走 `IPromptTemplateProvider` 新增键 `SmartConfigStructureRecognition`，模板正文（含 few-shot 一例标准表 + 一例仅规格表）注册进 `SystemPromptTemplateInitializer` 的默认模板集合（该初始化器现有写法照抄一条）。超时上限 15 秒。

- [ ] **Step 1: 写失败测试（只测 TryParseResult，不依赖真实 LLM）**——三个用例：合法 JSON 解析成功；带 ```json 围栏的解析成功；非法文本返回 false。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现 TryParseResult 与服务骨架**（RecognizeAsync 按镜像文件实现）
- [ ] **Step 4: 测试通过，注册 DI（与镜像服务同处注册），Commit** `git commit -am "feat: 新增 LLM 文档结构裁决服务"`

---

### Task 8: 识别编排器（四层流水线 + 决策规则）

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/TableStructureResult.cs`、`FieldRecognition.cs`、`StructureDecision.cs`
- Modify: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/DocumentIntelligenceService.cs`、`IDocumentIntelligenceService.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureDecisionTests.cs`

**新模型**：

```csharp
namespace AcceptanceSpecSystem.Core.Documents.Intelligence.Models;

public enum StructureDecision { AutoApply = 1, NeedsConfirmation = 2 }

/// <summary>单字段识别结论。</summary>
public sealed class FieldRecognition
{
    public int? ColumnIndex { get; init; }
    public double Confidence { get; init; }
    public IdentificationSource Source { get; init; }
    public string Reasoning { get; init; } = string.Empty;
}

/// <summary>单表完整结构识别结果。</summary>
public sealed class TableStructureResult
{
    public int TableIndex { get; init; }
    public bool IsRelevant { get; init; } = true;
    public double RelevanceScore { get; init; }
    public int HeaderRowIndex { get; init; }
    public int HeaderRowCount { get; init; } = 1;
    public int DataStartRowIndex { get; init; }
    public int? DataEndRowIndex { get; init; }
    public FieldRecognition Project { get; init; } = new();
    public FieldRecognition Specification { get; init; } = new();
    public FieldRecognition Acceptance { get; init; } = new();
    public FieldRecognition Remark { get; init; } = new();
    public bool IsSpecificationOnly { get; init; }
    public double SpecificationOnlyConfidence { get; init; }
    public StructureDecision Decision { get; init; }
    public List<string> PendingFields { get; init; } = new();   // 需确认字段名，如 "project"
    public string Fingerprint { get; init; } = string.Empty;
}
```

**接口新增方法**（保留原方法不破坏归档调用；`extraSynonyms` 由上层从 DB 规则组装传入，Core 不碰 DB）：

```csharp
Task<TableStructureResult> RecognizeTableAsync(
    TableInfo table,
    IReadOnlyDictionary<ColumnType, IReadOnlyList<string>>? extraSynonyms,
    CancellationToken cancellationToken = default);
```

**编排逻辑（决策规则是本任务核心，照此实现）**：

1. 表头行检测（归档已有）→ 得 headerRowIndex/headerRowCount，headers = 拼接表头行文本。
2. L1 `RuleBasedMappingStrategy.IdentifyAsync(headers, sampleRows, extraSynonyms)`。
3. 未决列表头集合 → L2 `IEmbeddingColumnMatcher.MatchAsync`，采纳非 Unknown 结果。
4. 仍未决字段中含 Specification，或表头行检测置信度 < 0.6 → L3 `ILlmStructureRecognitionService.RecognizeAsync(前10行)`；LLM 结果只用于填补空缺字段，不覆盖 L1/L2 已决字段；LLM 返回 null（超时/非法）→ 跳过。
5. 数据范围：DataStartRowIndex = headerRowIndex + headerRowCount；DataEndRowIndex：从表尾向上跳过「整行空 或 项目列为空且任意单元格含 合计/审核/批准/核准/确认 」的行，得最后数据行；若截掉行数 > 表行数 30% 则视为不可靠，DataEndRowIndex 置 null 并把 `"dataRange"` 加入 PendingFields。
6. **仅规格双向判定**：Project 未决时，取全表各列的 Project 类最高得分 maxP（来自 L1 关键词分与 L2 类得分的最大者）：maxP < 0.4 → `IsSpecificationOnly=true, SpecificationOnlyConfidence = 1 - maxP`；0.4 ≤ maxP < 阈值 → PendingFields 加 `"project"`（疑似有但没认出）。
7. **决策**：Specification.Confidence ≥ 0.85 且 (Project 已决 或 IsSpecificationOnly 且 SpecificationOnlyConfidence ≥ 0.85) 且 PendingFields 为空 → AutoApply；否则 NeedsConfirmation。
8. Fingerprint = `StructureFingerprint.Compute(headers, 列数)`。

- [ ] **Step 1: 写失败测试**（L2/L3 用测试内假实现注入；至少 5 个用例）：① 标准四列表 → AutoApply、无 Pending；② 规格列置信度 0.7 → NeedsConfirmation 且 Pending 含 "specification"；③ 无任何项目语义列（maxP<0.4）→ AutoApply + IsSpecificationOnly；④ 疑似项目列（maxP=0.6）→ NeedsConfirmation + Pending 含 "project"；⑤ 尾部含“合计/审核”行 → DataEndRowIndex 截到数据末行。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现编排**（注意文件 < 500 行；决策与数据范围检测可拆 `StructureDecisionEvaluator.cs` 私有静态类文件）
- [ ] **Step 4: 全量测试，Commit** `git commit -am "feat: 识别编排器实现四层流水线与结构决策"`

---

## Phase C：API 编排 + 学习沉淀

### Task 9: recognize 端点（全文档识别）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/SmartConfigurationAppService.cs`（重写为 recognize 编排；归档 auto-detect 逻辑删除）
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs`
- Create: `src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeApiTests.cs`

**DTO（完整定义，前后端契约）**：

```csharp
namespace AcceptanceSpecSystem.Api.DTOs;

public class SmartRecognizeRequest
{
    public string FileId { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
}

public class SmartRecognizeResponse
{
    public string FileId { get; set; } = string.Empty;
    /// <summary>"autoApply" | "needsConfirmation"（任一表需确认即为后者）</summary>
    public string OverallDecision { get; set; } = "needsConfirmation";
    public SmartTemplateHitDto? TemplateHit { get; set; }
    public List<SmartSheetResultDto> Sheets { get; set; } = new();
}

public class SmartTemplateHitDto
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int UseCount { get; set; }
}

public class SmartSheetResultDto
{
    public int SheetIndex { get; set; }
    public string SheetName { get; set; } = string.Empty;
    public List<SmartTableResultDto> Tables { get; set; } = new();
}

public class SmartTableResultDto
{
    public int TableIndex { get; set; }
    public bool IsRelevant { get; set; }
    public string Decision { get; set; } = "needsConfirmation";
    public int HeaderRowIndex { get; set; }
    public int HeaderRowCount { get; set; }
    public int DataStartRowIndex { get; set; }
    public int? DataEndRowIndex { get; set; }
    /// <summary>"projectSpecification" | "specificationOnly"</summary>
    public string MatchingMode { get; set; } = "projectSpecification";
    public SmartFieldDto? Project { get; set; }
    public SmartFieldDto Specification { get; set; } = new();
    public SmartFieldDto? Acceptance { get; set; }
    public SmartFieldDto? Remark { get; set; }
    public List<string> PendingFields { get; set; } = new();
    public string Fingerprint { get; set; } = string.Empty;
}

public class SmartFieldDto
{
    public int? ColumnIndex { get; set; }
    public double Confidence { get; set; }
    /// <summary>"template" | "rule" | "embedding" | "llm"</summary>
    public string Source { get; set; } = "rule";
    public string Reasoning { get; set; } = string.Empty;
}
```

**AppService 编排**（`RecognizeAsync(SmartRecognizeRequest, CancellationToken)`）：

1. 用现有文档解析服务（`DocumentServiceFactory` → parser，取法照抄归档 `SmartConfigurationAppService.AutoConfigureAsync` 里加载 tables 的写法）拿全部 Sheet/表。
2. L0：customerId 非空时，逐表算指纹查 `IDocumentTemplateRepository`（客户+指纹）；命中 → 该表直接由模板组装 `SmartTableResultDto`（Source=template，Confidence=1.0，Decision=autoApply），并回写 `UseCount++ / LastUsedAt`。
3. 未命中表：组装 extraSynonyms（查 `ColumnMappingRule`：`Enabled && (CustomerId == customerId || CustomerId == null)`，按 客户>全局 排序，映射 TargetField→ColumnType）→ 调 `IDocumentIntelligenceService.RecognizeTableAsync`。
4. 映射 Core 结果 → DTO；OverallDecision 汇总。
5. 控制器：`[HttpPost("recognize")]`，归档的 `auto-detect` action 与 `AutoDetectRequest` 删除。

- [ ] **Step 1: 写失败集成测试**——上传标准四列 docx（用测试项目现有 `CreateDocxBytes`/上传辅助方法，参照 `ExecutionHistoryApiTests`），调 `/api/smart-config/recognize`，断言 200、OverallDecision=autoApply、四字段列索引正确、Source=rule。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现编排 + 控制器**
- [ ] **Step 4: 全量测试，Commit** `git commit -am "feat: 智能识别 recognize 端点输出全文档结构"`

---

### Task 10: confirm 端点（模板沉淀 + 字典学习）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/SmartConfigurationAppService.cs`（若超 500 行拆 `SmartConfigurationAppService.Learning.cs` partial）
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs`、`SmartConfigController.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigConfirmApiTests.cs`

**DTO 追加**：

```csharp
public class SmartConfirmRequest
{
    public string FileId { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public List<SmartConfirmTableDto> Tables { get; set; } = new();
}

public class SmartConfirmTableDto
{
    public int SheetIndex { get; set; }
    public int TableIndex { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public string HeadersJson { get; set; } = "[]";
    public int HeaderRowIndex { get; set; }
    public int HeaderRowCount { get; set; }
    public int DataStartRowIndex { get; set; }
    public int? DataEndRowIndex { get; set; }
    public int? ProjectColumnIndex { get; set; }
    public int SpecificationColumnIndex { get; set; }
    public int? AcceptanceColumnIndex { get; set; }
    public int? RemarkColumnIndex { get; set; }
    public bool IsSpecificationOnly { get; set; }
    /// <summary>用户修正明细（仅用户改过的字段）</summary>
    public List<SmartCorrectionDto> Corrections { get; set; } = new();
}

public class SmartCorrectionDto
{
    /// <summary>"project" | "specification" | "acceptance" | "remark"</summary>
    public string Field { get; set; } = string.Empty;
    /// <summary>该列表头原文（学习词）</summary>
    public string HeaderText { get; set; } = string.Empty;
    public int ColumnIndex { get; set; }
}
```

**学习逻辑（照此实现）**：

- 模板：按 `(CustomerId, Fingerprint)` upsert `DocumentTemplate`（存在→整套字段覆盖 + `UseCount++`；不存在→新建，`TemplateName` = 客户名+日期，`UseCount=1`），`LastUsedAt=UtcNow`。
- 字典：每条 Correction 且 `HeaderText` 非空白 → 查 `ColumnMappingRule` 是否已存在 `(Pattern=HeaderText, TargetField=对应枚举, CustomerId=本客户)`；无则插入 `{ MatchMode=Equals, Source=Learned, CustomerId, Priority=100, Enabled=true }`。
- 全局升级：插入后统计 `Source=Learned && Pattern && TargetField` 相同、`CustomerId` 互异的条数 ≥ 2 且不存在同 Pattern 的全局行（CustomerId==null）→ 追加一条 `CustomerId=null` 的全局 Learned 规则。
- 全过程 try/catch：沉淀失败记日志返回 `learned=false`，**不抛给调用方**。

- [ ] **Step 1: 写失败集成测试**——① confirm 后同文件再 recognize，对应表 Decision=autoApply 且 Source=template（L0 命中）；② 带 Correction("project","管控要点",0) 的 confirm 后，DB 中存在 Learned 规则；③ 两个不同 customer 各 confirm 同词 → 出现全局行。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 全量测试，Commit** `git commit -am "feat: confirm 端点沉淀客户模板与自学习字典"`

---

### Task 11: 集成夹具矩阵 + 降级护栏

**Files:**
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeMatrixTests.cs`

- [ ] **Step 1: 写四类夹具测试**（全部用测试内构造字节，xlsx 用 ClosedXML，docx 用 OpenXml，构造法参照 `ExecutionHistoryApiTests.CreateDocxBytes` 与 Excel 导入测试）：
  1. 怪表头 xlsx（“管控要点/判定基准/OK?NG/说明”）→ 200 且 needsConfirmation（无学习词、Fake Embedding 不达标时的预期路径），PendingFields 非空；
  2. 多 Sheet xlsx（Sheet1 标准 → autoApply；Sheet2 怪表头 → needsConfirmation）→ 按表独立决策；
  3. 仅规格 docx（表头只有“规格/验收/备注”）→ MatchingMode=specificationOnly 且 autoApply；
  4. 空文档/无表格 → 200 + Sheets 空 + OverallDecision=needsConfirmation（前端据此落高级模式），**不是 500**。
- [ ] **Step 2: 跑测试，修复实现直到全绿**
- [ ] **Step 3: 全量测试 + 架构边界测试确认，Commit** `git commit -am "test: 智能识别四类文档夹具矩阵与降级护栏"`

---

## Phase D：前端两步流

### Task 12: API 封装 + 识别 composable

**Files:**
- Create: `web/src/api/smart-config.ts`
- Create: `web/src/views/data-import/composables/useSmartRecognition.ts`（两链路共享，放 data-import 下由 smart-fill 引入，或提升到 `web/src/composables/`——按后者）

```typescript
// web/src/api/smart-config.ts
import { http } from "@/utils/http";

export interface SmartFieldResult {
  columnIndex: number | null;
  confidence: number;
  source: "template" | "rule" | "embedding" | "llm";
  reasoning: string;
}

export interface SmartTableResult {
  tableIndex: number;
  isRelevant: boolean;
  decision: "autoApply" | "needsConfirmation";
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex: number | null;
  matchingMode: "projectSpecification" | "specificationOnly";
  project: SmartFieldResult | null;
  specification: SmartFieldResult;
  acceptance: SmartFieldResult | null;
  remark: SmartFieldResult | null;
  pendingFields: string[];
  fingerprint: string;
}

export interface SmartSheetResult {
  sheetIndex: number;
  sheetName: string;
  tables: SmartTableResult[];
}

export interface SmartRecognizeResponse {
  fileId: string;
  overallDecision: "autoApply" | "needsConfirmation";
  templateHit: { templateId: number; templateName: string; useCount: number } | null;
  sheets: SmartSheetResult[];
}

export interface SmartCorrection {
  field: "project" | "specification" | "acceptance" | "remark";
  headerText: string;
  columnIndex: number;
}

export interface SmartConfirmTable {
  sheetIndex: number;
  tableIndex: number;
  fingerprint: string;
  headersJson: string;
  headerRowIndex: number;
  headerRowCount: number;
  dataStartRowIndex: number;
  dataEndRowIndex: number | null;
  projectColumnIndex: number | null;
  specificationColumnIndex: number;
  acceptanceColumnIndex: number | null;
  remarkColumnIndex: number | null;
  isSpecificationOnly: boolean;
  corrections: SmartCorrection[];
}

export const recognizeDocument = (fileId: string, customerId?: number) =>
  http.request<{ code: number; data: SmartRecognizeResponse; message?: string }>(
    "post",
    "/api/smart-config/recognize",
    { data: { fileId, customerId } }
  );

export const confirmRecognition = (
  fileId: string,
  customerId: number,
  tables: SmartConfirmTable[]
) =>
  http.request<{ code: number; data: { learned: boolean }; message?: string }>(
    "post",
    "/api/smart-config/confirm",
    { data: { fileId, customerId, tables } }
  );
```

composable `useSmartRecognition`：状态机 `idle | recognizing | done | failed`，暴露 `recognize(fileId, customerId)`、`confirm(...)`、`result`、`failed`（失败仅置状态供页面落高级模式，内部 catch 不上抛）。代码 ≤ 80 行，`http.request` 写法与 `web/src/api/document.ts` 现有函数保持一致（先读该文件对齐真实封装签名，若 `http.request` 泛型形参不同以现文件为准）。

- [ ] **Step 1: 写 API 封装与 composable**
- [ ] **Step 2: `cd web && pnpm typecheck` 通过**
- [ ] **Step 3: Commit** `git commit -am "feat: 前端智能识别 API 封装与共享 composable"`

---

### Task 13: 摘要横幅 + 确认卡组件

**Files:**
- Create: `web/src/components/SmartRecognition/RecognitionSummaryBanner.vue`
- Create: `web/src/components/SmartRecognition/RecognitionConfirmCard.vue`

**Banner**（≤ 60 行）：props `result: SmartRecognizeResponse`；el-alert 样式一行摘要：“已自动识别 N 张表 · 来源：{模板名 | AI 识别}”，可展开列出每表映射 chips。

**ConfirmCard**：props `sheets`（仅含 needsConfirmation 表）、`fileId`；el-tabs 按表分签；每签内：
- 顶部若 `pendingFields.includes('project')` 或 `matchingMode==='specificationOnly'` 显示黄色提示条：“未识别到项目列，将按仅规格匹配——确认，或在下方指定项目列”。
- 主体复用 `TablePreview`（`web/src/views/data-import/components/TablePreview.vue`，传 `headerRowIndex/headerRowCount/dataStartRowIndex`）展示前 10 行；上方四个 el-select（项目/规格/验收/备注），选项为该表各列（`列N：表头文本`），初值取识别结果，`pendingFields` 对应的 select 加 `warning` 样式类；
- 底部主按钮「确认，继续」→ emit `confirm(tables: SmartConfirmTable[])`（含用户改动对应的 corrections：与初值不同的字段生成 `{field, headerText, columnIndex}`）；
- 右上角「手动配置」→ emit `manual`。

- [ ] **Step 1: 实现两组件**（选项/初值/corrections 逻辑放组件内 `computed`+`ref`，勿引入新全局状态）
- [ ] **Step 2: `pnpm typecheck` + `pnpm lint:eslint` 通过**
- [ ] **Step 3: Commit** `git commit -am "feat: 识别摘要横幅与确认卡组件"`

---

### Task 14: 数据导入链路接入

**Files:**
- Modify: `web/src/views/data-import/index.vue` 及其步骤编排 composable（以文件内实际步骤数组为准）
- Modify: `web/src/views/data-import/components/DataImportStepUpload.vue`（上传完成回调后触发识别）

**改造内容**：
1. 步骤数组由 5 步收敛为 3 步：上传（含客户/制程）→ 确认/预览 → 完成；「选表」「列映射」两步的组件（`DataImportStepTableSelect.vue`、`DataImportStepMapping.vue`）不删文件，移入确认页「手动配置」抽屉（el-drawer）作为高级模式。
2. 上传完成 → `useSmartRecognition.recognize(fileId, customerId)`；加载态文案按 `source` 分层显示（“命中客户模板…”/“AI 分析表头中…”用 el-loading 文本）。
3. `overallDecision==='autoApply'` → 直接以识别结果填充既有导入预览参数（表索引/行范围/列索引），渲染 Banner + 预览；`needsConfirmation` → 渲染 ConfirmCard，`confirm` 事件回调里调 `confirmRecognition` 后进预览；`failed` 或 Sheets 为空 → ElMessage 提示并直接打开高级模式抽屉。
4. 「确认导入」沿用既有 `POST /api/documents/import` 调用，参数来源换成识别/确认结果。

- [ ] **Step 1: 实现改造**
- [ ] **Step 2: `pnpm typecheck` 通过；启动前后端手工走一遍标准 xlsx 导入（上传→自动识别→预览→导入成功）**
- [ ] **Step 3: Commit** `git commit -am "feat: 数据导入收敛为智能识别两步流"`

---

### Task 15: 智能填充链路接入

**Files:**
- Modify: `web/src/views/smart-fill/index.vue`、`SmartFillUploadStep.vue`、`SmartFillTableStep.vue`

**改造内容**：
1. 上传步完成 → `recognize`；AutoApply 表自动组装既有 `previewTables` 配置项（`BatchTableConfigItem`，字段对应：tableIndex/行范围/四列索引；`matchingMode==='specificationOnly'` 时项目列传 null 并设置既有仅规格开关字段——以 `batchTableConfig.types.ts` 内实际字段名为准），**直接触发既有“开始匹配”动作**；
2. 存在 needsConfirmation 表 → 表配置步只显示 ConfirmCard（AutoApply 表折叠进 Banner），确认后并入配置并开跑；
3. `BatchTableConfig.vue` 保留为「手动配置」抽屉（高级模式）；
4. 识别失败 → 落回现有 `SmartFillTableStep` 手动流程。

- [ ] **Step 1: 实现改造**
- [ ] **Step 2: `pnpm typecheck`；手工走一遍：标准文档上传→自动识别→自动开跑匹配→预览出结果；仅规格文档→匹配模式为仅规格**
- [ ] **Step 3: Commit** `git commit -am "feat: 智能填充收敛为智能识别两步流"`

---

### Task 16: 回归 + 真实样本验收

- [ ] **Step 1: 后端全量** `dotnet test AcceptanceSpecSystem.sln -c Debug` 全绿（含架构边界测试）。
- [ ] **Step 2: 前端** `cd web && pnpm typecheck && pnpm test && pnpm build` 全绿。
- [ ] **Step 3: 真实样本手工验收**——用仓库根 `淮安庆鼎.xlsx`、`提供测试的文档/` 内样本各走一遍导入与填充：记录每份文档 直达/确认卡/高级模式 的落点与识别正确性；同客户第二次上传同结构文档必须直达（L0）。
- [ ] **Step 4: 结果达标**（老客户重复结构 100% 直达；新结构 ≥70% 免修正）**后 Commit 收尾** `git commit -am "test: 智能识别真实样本回归验收"`（如有夹具/文档产出一并提交）。

---

## Self-Review 记录

- 规格覆盖：设计文档 §2 引擎四层 → Task 4-8；§3 前端 → Task 12-15；§4 自学习 → Task 10；§5 API/降级/迁移 → Task 1-3, 9-11；§6 边界 → Task 8(数据范围/无表头走 L3)、Task 11(空文档)；§7 测试 → 各任务 TDD + Task 11/16。仅规格场景 → Task 2(可空列)/8(双向判定)/11(夹具3)/15(模式下发)。无遗漏。
- 占位符：无 TBD/TODO；引用「镜像现有实现」处均给出确切镜像文件位置与差异点。
- 类型一致性：`SmartTableResultDto` ↔ `SmartTableResult`(TS) 字段一一对应；`RecognizeTableAsync` 签名在 Task 8 定义、Task 9 使用一致；`ColumnMappingRuleSource` 在 Task 3 定义、Task 9/10 使用一致。
