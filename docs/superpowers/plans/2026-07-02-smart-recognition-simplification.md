# 智能结构识别·流程极简化 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 上传文档后自动识别全部结构配置（表/表头/数据范围/四列映射），高置信直达、低置信行内确认卡、确认自动沉淀模板与字典，数据导入与智能填充共用。

**Architecture:** 三层识别流水线（L0 客户模板 → L1 规则字典 → L3 LLM 裁决，不设 Embedding 层）+ AutoApply 前的确定性结果体检，落在 `Core/Documents/Intelligence`；跨资源编排、模板命中和学习写回放在 `Application/Services/SmartConfigurationAppService`，`Api` 控制器只做 HTTP 适配；前端两条链路收敛为「上传 → 结果页」两步流。归档分支 `origin/feat/smart-auto-configuration` 的后端骨架捡回复用，前端改动不捡。

**Tech Stack:** .NET 8 / EF Core (MySQL, 测试 SQLite) / Semantic Kernel（复用 LLM 服务族 DI 模式）/ Vue 3 + Element Plus。

**设计文档:** `docs/superpowers/specs/2026-07-02-smart-recognition-simplification-design.md`

**工作分支:** `feat/smart-recognition-simplification`（已存在，含设计文档提交）

---

## Phase 0：OpenSpec 变更门禁

### Task 0: 补 OpenSpec change 并严格校验

**Files:**
- Create: `openspec/changes/add-smart-recognition-simplification/proposal.md`
- Create: `openspec/changes/add-smart-recognition-simplification/tasks.md`
- Create/Modify delta specs under:
  - `openspec/changes/add-smart-recognition-simplification/specs/api/spec.md`
  - `openspec/changes/add-smart-recognition-simplification/specs/user-interface/spec.md`
  - `openspec/changes/add-smart-recognition-simplification/specs/data-storage/spec.md`
  - `openspec/changes/add-smart-recognition-simplification/specs/matching-engine/spec.md`（仅覆盖智能填充识别接入与仅规格模式约束）

- [ ] **Step 1: 读取当前 specs**

```bash
openspec list
openspec list --specs
openspec show api --type spec
openspec show user-interface --type spec
openspec show data-storage --type spec
openspec show matching-engine --type spec
openspec show architecture --type spec
```

- [ ] **Step 2: 创建 change 文件**

按 `openspec/AGENTS.md` 要求写 proposal/tasks/delta specs。要求至少覆盖：
- `POST /api/smart-config/recognize` 和 `confirm`；
- 扁平 `tables` 响应、`fileId` 为数字、索引口径；
- 客户模板、学习词、`ColumnMappingRule.Source/CustomerId`；
- 前端两步流、确认卡、高级模式兜底；
- 现有数据导入 5 步状态机重排：`DataImportStepTarget` 目标选择前移到上传/归属区，`useDataImportPage`、`useDataImportStore`、`dataImport.types.ts` 与批量导入构造逻辑同步调整；
- 数据导入仅在满足现有导入接口必填列时直达；
- 智能填充混合 `matchingMode` 必须拆请求或改后端支持表级模式。

- [ ] **Step 3: 严格校验**

```bash
openspec validate add-smart-recognition-simplification --strict
```

Expected: validation passed。

- [ ] **Step 4: Commit**

```bash
git add openspec/changes/add-smart-recognition-simplification
git commit -m "docs: 补智能结构识别 OpenSpec 变更"
```

---

## 全局约束（每个任务都要遵守）

- 架构边界测试是硬门禁：Core 不引用上层；`Api/Services` 服务必须 `public interface IXxxAppService` + `public sealed class XxxAppService : IXxxAppService`，`AddScoped<IXxx, Xxx>()` 注册；控制器注入接口、透传 `CancellationToken`；单文件 < 500 行。
- Schema 变更只走 EF Core 迁移：`dotnet ef migrations add <Name> -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api`。
- 提交信息 `类型: 中文描述`。
- 每个任务完成后跑 `dotnet test AcceptanceSpecSystem.sln -c Debug`（前端任务另加 `cd web && pnpm typecheck`），全绿才提交。
- 含新增文件的任务必须显式 `git add <new-files>`，不得只用 `git commit -am`。
- 数据导入直达必须满足现有导入接口：Word 当前要求项目/规格/验收/备注四列齐全；Excel 当前要求项目/规格列齐全。仅规格或缺验收/备注时进入确认/高级模式，除非本计划另增后端导入接口改造任务。

---

## Phase A：捡回归档骨架 + 数据层

### Task 1: 捡回归档分支后端骨架

**Files:**
- Create（从归档分支检出）:
  - `src/AcceptanceSpecSystem.Core/Documents/Intelligence/`（整个目录：`DocumentIntelligenceService.cs`、`IDocumentIntelligenceService.cs`、`Models/*.cs`、`Strategies/*.cs`）
  - `src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/IDocumentTemplateRepository.cs`
  - `src/AcceptanceSpecSystem.Data/Repositories/DocumentTemplateRepository.cs`
  - `src/AcceptanceSpecSystem.Application/Services/DocumentTemplateAppService.cs`
  - `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`
  - `src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs`

- [ ] **Step 1: 检出归档文件（不检出归档迁移，迁移本地重新生成）**

```bash
git checkout origin/feat/smart-auto-configuration -- \
  src/AcceptanceSpecSystem.Core/Documents/Intelligence \
  src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs \
  src/AcceptanceSpecSystem.Data/Repositories/IDocumentTemplateRepository.cs \
  src/AcceptanceSpecSystem.Data/Repositories/DocumentTemplateRepository.cs \
  src/AcceptanceSpecSystem.Application/Services/DocumentTemplateAppService.cs \
  src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs \
  src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs
```

- [ ] **Step 2: 补 DbContext 与 DI 注册**

对照归档分支同名文件，把缺的注册行抄回来（归档分支这几处已写好，直接对照）：

```bash
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Data/Context/AppDbContext.cs | grep -n "DocumentTemplate"
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs | grep -n -B2 -A2 "DocumentTemplate\|Intelligence"
git show origin/feat/smart-auto-configuration:src/AcceptanceSpecSystem.Api/Program.cs | grep -n -B2 -A2 "SmartConfig\|DocumentTemplate\|Intelligence"
```

按 grep 结果把 `DbSet<DocumentTemplate>`、实体配置、`IDocumentTemplateRepository`/`IDocumentIntelligenceService`/`IRuleBasedMappingStrategy`/`SmartConfigurationAppService` 等注册加到当前分支对应文件。注意：当前架构规格要求跨资源工作流放入 Application 层，不要在 `Api/Services` 新增智能结构识别编排服务。

注意：归档 `DocumentIntelligenceService` 仍是旧骨架，只支持 `IdentifyTargetTableAsync` + `AutoConfigureAsync` 单目标表流程；Task 8 必须新增/重构 `RecognizeTableAsync`，不能把归档服务当作全文档逐表识别实现。

- [ ] **Step 3: 本地重新生成迁移**

```bash
dotnet ef migrations add AddDocumentTemplate -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
```

Expected: 生成的 Up() 只含 CreateTable "DocumentTemplates"。

- [ ] **Step 4: 编译 + 全量测试**

```bash
dotnet build AcceptanceSpecSystem.sln -c Debug && dotnet test AcceptanceSpecSystem.sln -c Debug
```

Expected: 可能因架构边界、接口注册或旧 `SmartConfigurationAppService` 签名报错；按当前 Application 编排边界修到编译与测试通过。不要假设归档代码可无修改落地。

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

public class DocumentTemplateRepositoryTests : TestBase
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
            UsageCount = 1,                     // 归档字段名，沿用而不是新增 UseCount
            LastUsedAt = DateTime.UtcNow        // 新增
        };
        Context.Set<DocumentTemplate>().Add(template);
        await Context.SaveChangesAsync();

        var found = Context.Set<DocumentTemplate>()
            .Single(t => t.CustomerId == 1 && t.HeadersFingerprint == "fp-001");
        found.IsSpecificationOnly.Should().BeTrue();
        found.ProjectColumnIndex.Should().BeNull();
        found.DataStartRowIndex.Should().Be(1);
        found.UsageCount.Should().Be(1);
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

    /// <summary>命中/确认次数（归档实体已有字段，沿用命名）</summary>
    public int UsageCount { get; set; }

    /// <summary>最近使用时间</summary>
    public DateTime? LastUsedAt { get; set; }
```

- [ ] **Step 4: 生成迁移 + 全量测试通过**

```bash
dotnet ef migrations add ExtendDocumentTemplateForStructure -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet test AcceptanceSpecSystem.sln -c Debug
```

- [ ] **Step 5: Commit**

```bash
git add src/AcceptanceSpecSystem.Data/Entities/DocumentTemplate.cs src/AcceptanceSpecSystem.Data/Migrations tests/AcceptanceSpecSystem.Data.Tests/DocumentTemplateRepositoryTests.cs
git commit -m "feat: 扩展文档模板实体支撑完整结构与仅规格模式"
```

---

### Task 3: ColumnMappingRule 扩展（学习来源 + 客户域）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Data/Entities/ColumnMappingRule.cs`
- Modify: `src/AcceptanceSpecSystem.Data/Repositories/IColumnMappingRuleRepository.cs`、`ColumnMappingRuleRepository.cs`（当前 `GetEnabledOrderedAsync()` 无客户参数）
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/ColumnMappingRuleDtos.cs`（若 DTO 位于其他文件，以 `rg "ColumnMappingRuleDto"` 结果为准）
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/ColumnMappingRulesController.cs`
- Modify: `web/src/api/column-mapping-rules.ts`
- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`、`web/src/views/smart-fill/composables/useSmartFillUploadedTables.ts`（当前都调用无参 `getEffectiveColumnMappingRules()`）
- Modify: 列映射规则配置页组件（以 `rg "getColumnMappingRules|ColumnMappingRule"` 定位）
- Test: `tests/AcceptanceSpecSystem.Data.Tests/ColumnMappingRuleLearnedFieldsTests.cs`（新建，写法同 Task 2 Step 1，断言 `Source`/`CustomerId` 往返存取）
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ColumnMappingRuleLearningApiTests.cs`

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

同时补齐：
- `ColumnMappingRuleDto` 输出 `source/customerId`；
- Create/Update 请求允许维护 `source/customerId`，人工配置默认 `Manual`；
- `GET /api/column-mapping-rules/effective?customerId=` 返回 `Enabled && (CustomerId == customerId || CustomerId == null)`，排序为客户规则优先、Priority 降序；
- 前端 API 类型增加 `ColumnMappingRuleSource`、`source`、`customerId`，`getEffectiveColumnMappingRules(customerId?: number)` 透传查询参数；
- 数据导入和智能填充加载 Word 列映射规则时传当前 `selectedCustomerId`；客户未选时只加载全局规则，不得把客户学习词当全局规则使用；
- 配置页显示来源标签（内置/学习/人工）和客户域。

```bash
dotnet ef migrations add AddColumnMappingRuleLearning -p src/AcceptanceSpecSystem.Data -s src/AcceptanceSpecSystem.Api
dotnet test AcceptanceSpecSystem.sln -c Debug
```

- [ ] **Step 5: Commit**

```bash
git add src/AcceptanceSpecSystem.Data/Entities/ColumnMappingRule.cs src/AcceptanceSpecSystem.Data/Repositories/IColumnMappingRuleRepository.cs src/AcceptanceSpecSystem.Data/Repositories/ColumnMappingRuleRepository.cs src/AcceptanceSpecSystem.Data/Migrations src/AcceptanceSpecSystem.Api web/src tests/AcceptanceSpecSystem.Data.Tests/ColumnMappingRuleLearnedFieldsTests.cs tests/AcceptanceSpecSystem.Api.Tests/ColumnMappingRuleLearningApiTests.cs
git commit -m "feat: 列映射规则支持学习来源与客户域"
```

---

## Phase B：识别引擎三层 + 结果体检（Core，纯单测）

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

- [ ] **Step 4: 测试通过后 Commit** `git add src/AcceptanceSpecSystem.Core/Documents/Intelligence/StructureFingerprint.cs tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureFingerprintTests.cs && git commit -m "feat: 新增文档结构指纹计算"`

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
- [ ] **Step 4: 全量 Core 测试通过，Commit** `git add src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/IRuleBasedMappingStrategy.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/RuleBasedMappingStrategy.cs tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/RuleBasedMappingStrategyTests.cs && git commit -m "feat: 规则映射策略支持外部词典融合"`

---

### Task 6: 结果验证器（AutoApply 前的确定性体检）

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/StructureSanityValidator.cs`
- Test: `tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureSanityValidatorTests.cs`

**背景**：LLM 自报置信度不可信，最坏失败模式是“自信地识别错 → 静默填错列”。任何层的输出在 AutoApply 前必须通过纯规则体检；体检失败强制降为 NeedsConfirmation。

**接口与实现（完整代码，输入用原始类型，不依赖 Task 8 的模型）**：

```csharp
namespace AcceptanceSpecSystem.Core.Documents.Intelligence;

/// <summary>体检输入：候选结构 + 表格原始行。</summary>
public sealed class SanityCheckInput
{
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public int ColumnCount { get; init; }
    public int DataStartRowIndex { get; init; }
    public int? DataEndRowIndex { get; init; }
    public int? ProjectColumnIndex { get; init; }
    public int? SpecificationColumnIndex { get; init; }
    public int? AcceptanceColumnIndex { get; init; }
    public int? RemarkColumnIndex { get; init; }
}

public sealed class SanityCheckResult
{
    public bool Passed { get; init; }
    public List<string> Failures { get; init; } = new();
}

/// <summary>AutoApply 前的确定性体检：拦截“自信但错误”的识别结果。</summary>
public static class StructureSanityValidator
{
    private const double MinSpecificationNonEmptyRate = 0.6;

    public static SanityCheckResult Validate(SanityCheckInput input)
    {
        var failures = new List<string>();

        // 1) 规格列必须存在且索引合法
        if (input.SpecificationColumnIndex is not int specCol || specCol < 0 || specCol >= input.ColumnCount)
        {
            failures.Add("规格列缺失或索引越界");
            return new SanityCheckResult { Passed = false, Failures = failures };
        }

        // 2) 列索引不越界、互不重复
        var indexes = new[] { input.ProjectColumnIndex, input.SpecificationColumnIndex, input.AcceptanceColumnIndex, input.RemarkColumnIndex }
            .Where(i => i.HasValue).Select(i => i!.Value).ToList();
        if (indexes.Any(i => i < 0 || i >= input.ColumnCount)) failures.Add("存在越界列索引");
        if (indexes.Count != indexes.Distinct().Count()) failures.Add("列索引重复");

        // 3) 数据区必须存在非空行
        var end = input.DataEndRowIndex ?? input.Rows.Count - 1;
        var dataRows = input.Rows
            .Skip(input.DataStartRowIndex)
            .Take(Math.Max(0, end - input.DataStartRowIndex + 1))
            .ToList();
        if (dataRows.Count == 0 || dataRows.All(r => r.All(string.IsNullOrWhiteSpace)))
        {
            failures.Add("数据区无有效数据行");
        }
        else
        {
            // 4) 规格列非空率
            var nonEmpty = dataRows.Count(r => specCol < r.Count && !string.IsNullOrWhiteSpace(r[specCol]));
            if ((double)nonEmpty / dataRows.Count < MinSpecificationNonEmptyRate)
                failures.Add($"规格列非空率不足（{nonEmpty}/{dataRows.Count}）");

            // 5) 识别出项目列时，规格列平均长度应不小于项目列（规格通常是长文本）
            if (input.ProjectColumnIndex is int projCol && projCol >= 0 && projCol < input.ColumnCount)
            {
                double AvgLen(int col) => dataRows
                    .Where(r => col < r.Count && !string.IsNullOrWhiteSpace(r[col]))
                    .Select(r => (double)r[col].Trim().Length)
                    .DefaultIfEmpty(0).Average();
                if (AvgLen(specCol) > 0 && AvgLen(specCol) < AvgLen(projCol) * 0.5)
                    failures.Add("规格列平均文本长度显著小于项目列，疑似列判反");
            }
        }

        return new SanityCheckResult { Passed = failures.Count == 0, Failures = failures };
    }
}
```

- [ ] **Step 1: 写失败测试**——至少 5 个用例：① 正常四列数据 → Passed；② 规格列整列空 → 拦截且 Failures 含“非空率”；③ 项目列与规格列同索引 → 拦截“重复”；④ 数据区全空行 → 拦截；⑤ 项目/规格判反（项目列全长文本、规格列全 2 字短词）→ 拦截“疑似列判反”。
- [ ] **Step 2: 跑测试失败** `dotnet test tests/AcceptanceSpecSystem.Core.Tests -c Debug --filter "FullyQualifiedName~StructureSanityValidatorTests"`
- [ ] **Step 3: 按上述代码实现**
- [ ] **Step 4: 测试通过，Commit** `git add src/AcceptanceSpecSystem.Core/Documents/Intelligence/StructureSanityValidator.cs tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureSanityValidatorTests.cs && git commit -m "feat: 新增结构识别结果确定性体检"`

---

### Task 7: L3 LLM 结构裁决服务

**Files:**
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/ILlmStructureRecognitionService.cs`、`LlmStructureRecognitionService.cs`
- Create: `src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/LlmStructureResult.cs`
- Modify: `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs`（注册 `ILlmStructureRecognitionService`）
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

实现方式：**镜像现有 `ILlmEquivalenceAdjudicationService` 实现类的全部管线写法**（同目录 `Core/Matching/Services/` 下找到其实现文件：chat 服务获取、超时、重试一次、JSON 容错剥离 ```json 围栏）；Prompt 模板走 `IPromptTemplateProvider` 新增 `PromptTemplateScene.SmartConfigStructureRecognition`。必须同步补齐 Core/Data 两套 `PromptTemplateScene` 枚举、`CoreProviderAdapters.ToDataScene`、`SystemPromptTemplateInitializer.ToDataPromptTemplateScene`、`PromptTemplatesController.ToDataPromptTemplateScene`、`PromptTemplateValidationService` 场景校验、`PromptTemplateCatalog` 默认模板与相关单测。模板正文含 few-shot 一例标准表 + 一例仅规格表。超时上限 15 秒。

- [ ] **Step 1: 写失败测试（只测 TryParseResult，不依赖真实 LLM）**——三个用例：合法 JSON 解析成功；带 ```json 围栏的解析成功；非法文本返回 false。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现 TryParseResult 与服务骨架**（RecognizeAsync 按镜像文件实现）
- [ ] **Step 4: 测试通过，注册 DI（与镜像服务同处注册），Commit** `git add src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/ILlmStructureRecognitionService.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/Strategies/LlmStructureRecognitionService.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/LlmStructureResult.cs src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/LlmStructureParseTests.cs && git commit -m "feat: 新增 LLM 文档结构裁决服务"`

---

### Task 8: 识别编排器（三层流水线 + 体检 + 决策规则）

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
3. 仍未决字段中含 Specification，或表头行检测置信度 < 0.6 → L3 `ILlmStructureRecognitionService.RecognizeAsync(前10行)`；LLM 结果只用于填补空缺字段，不覆盖 L1 已决字段；LLM 返回 null（超时/非法）→ 跳过。
4. 数据范围：DataStartRowIndex = headerRowIndex + headerRowCount；DataEndRowIndex：从表尾向上跳过「整行空 或 项目列为空且任意单元格含 合计/审核/批准/核准/确认 」的行，得最后数据行；若截掉行数 > 表行数 30% 则视为不可靠，DataEndRowIndex 置 null 并把 `"dataRange"` 加入 PendingFields。
5. **仅规格双向判定**：Project 未决时，取全表各列的 Project 类最高得分 maxP（来自 L1 关键词得分；L3 参与且返回 projectColumn=null 视为“确无项目列”的佐证，maxP 取 L1 值）：maxP < 0.4 → `IsSpecificationOnly=true, SpecificationOnlyConfidence = 1 - maxP`；0.4 ≤ maxP < 阈值 → PendingFields 加 `"project"`（疑似有但没认出）。
6. **体检前置**：组装 `SanityCheckInput` 过 `StructureSanityValidator.Validate`（Task 6）；不通过 → 强制 NeedsConfirmation，Failures 并入对应字段 Reasoning 与 PendingFields。
7. **决策**：体检通过且 Specification.Confidence ≥ 0.85 且 (Project 已决 或 IsSpecificationOnly 且 SpecificationOnlyConfidence ≥ 0.85) 且 PendingFields 为空 → AutoApply；否则 NeedsConfirmation。
8. Fingerprint = `StructureFingerprint.Compute(headers, 列数)`。

- [ ] **Step 1: 写失败测试**（L3 用测试内假实现注入；至少 6 个用例）：① 标准四列表 → AutoApply、无 Pending；② 规格列置信度 0.7 → NeedsConfirmation 且 Pending 含 "specification"；③ 无任何项目语义列（maxP<0.4）→ AutoApply + IsSpecificationOnly；④ 疑似项目列（maxP=0.6）→ NeedsConfirmation + Pending 含 "project"；⑤ 尾部含“合计/审核”行 → DataEndRowIndex 截到数据末行；⑥ 各字段高置信但规格列数据全空（体检不过）→ 强制 NeedsConfirmation。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现编排**（注意文件 < 500 行；决策与数据范围检测可拆 `StructureDecisionEvaluator.cs` 私有静态类文件）
- [ ] **Step 4: 全量测试，Commit** `git add src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/TableStructureResult.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/FieldRecognition.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/Models/StructureDecision.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/DocumentIntelligenceService.cs src/AcceptanceSpecSystem.Core/Documents/Intelligence/IDocumentIntelligenceService.cs tests/AcceptanceSpecSystem.Core.Tests/Documents/Intelligence/StructureDecisionTests.cs && git commit -m "feat: 识别编排器实现三层流水线与体检决策"`

---

## Phase C：API 编排 + 学习沉淀

### Task 9: recognize 端点（全文档识别）

**Files:**
- Modify/Create: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`（重写为 recognize/confirm 编排；归档 auto-detect 逻辑删除）
- Modify: `src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs`（注册 `SmartConfigurationAppService`、智能识别相关 Application 依赖）
- Modify: `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs`（注册智能识别相关 Core 服务、`ILlmStructureRecognitionService`，如当前 DI 归属在 API 层）
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs`
- Create: `src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeApiTests.cs`

**DTO（完整定义，前后端契约）**：

```csharp
namespace AcceptanceSpecSystem.Api.DTOs;

public class SmartRecognizeRequest
{
    public int FileId { get; set; }
    public int? CustomerId { get; set; }
}

public class SmartRecognizeResponse
{
    public int FileId { get; set; }
    /// <summary>"autoApply" | "needsConfirmation"（任一表需确认即为后者）</summary>
    public string OverallDecision { get; set; } = "needsConfirmation";
    public SmartTemplateHitDto? TemplateHit { get; set; }
    /// <summary>扁平表格列表：沿用现有 TableInfo(Index + Name)，Excel 的 Name 为工作表名，Word 的 Name 通常为空。</summary>
    public List<SmartTableResultDto> Tables { get; set; } = new();
}

public class SmartTemplateHitDto
{
    public int TemplateId { get; set; }
    public string TemplateName { get; set; } = string.Empty;
    public int UsageCount { get; set; }
}

public class SmartTableResultDto
{
    public int TableIndex { get; set; }
    /// <summary>Excel：工作表名称；Word：通常为空。</summary>
    public string TableName { get; set; } = string.Empty;
    /// <summary>识别所用表头文本，前端用于确认卡列选项、headersJson 与修正词。</summary>
    public List<string> Headers { get; set; } = new();
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
    /// <summary>"template" | "rule" | "llm"</summary>
    public string Source { get; set; } = "rule";
    public string Reasoning { get; set; } = string.Empty;
}
```

**索引口径硬约束**：
- 识别核心与 DTO 的 `ColumnIndex`、`HeaderRowIndex`、`DataStartRowIndex`、`DataEndRowIndex` 统一使用解析后 `TableData` 的 0-based 相对索引。
- 接 Word 导入/预览时可直接映射到 `ColumnMappingDto`。
- 接 Excel 导入时必须转换为现有 `ExcelImportDataRequest` 口径：列号/行号均为 1-based 工作表绝对坐标。列号 = `ColumnIndex + TableInfo.UsedRangeStartColumn`；`HeaderRowStart/DataStartRow/DataEndRow` = 相对行索引 + `TableInfo.UsedRangeStartRow`。不得把 0-based 识别结果直接传给 Excel 接口。

**AppService 编排**（`RecognizeAsync(SmartRecognizeRequest, CancellationToken)`）：

1. 用现有文档解析服务（`DocumentServiceFactory` → parser，取法照抄归档 `SmartConfigurationAppService.AutoConfigureAsync` 里加载 tables 的写法）拿扁平 `TableInfo` 列表。不得新造 `Sheet -> Tables` 二级结构：Excel 的一个工作表就是一个 `TableInfo(Index + Name)`，Word 是顶层表格序列。
2. L0：customerId 非空时，逐表在**前 5 个候选表头行**上分别试算指纹查 `IDocumentTemplateRepository`（客户+指纹），任一命中即中（容错表头行漂移）；命中 → 该表直接由模板组装 `SmartTableResultDto`（Source=template，Confidence=1.0，Decision=autoApply），并回写 `UsageCount++ / LastUsedAt`。
3. 未命中表：组装 extraSynonyms（查 `ColumnMappingRule`：`Enabled && (CustomerId == customerId || CustomerId == null)`，按 客户>全局 排序，映射 TargetField→ColumnType）→ 调 `IDocumentIntelligenceService.RecognizeTableAsync`。
4. **整体 LLM 预算**：用 `Stopwatch` 累计，L3 累计耗时超过 20 秒后，剩余未处理表**不再进 L3**，直接以 L1 结果组装并置 Decision=needsConfirmation（依据说明写“LLM 预算耗尽，请人工确认”）——接口整体永远有界返回。
5. 映射 Core 结果 → DTO；`FileId` 保持 `int`；`TableIndex` 沿用 `TableInfo.Index`；`TableName` 使用 `TableInfo.Name`；`Headers` 使用识别时的拼接表头；OverallDecision 汇总。
6. 控制器：`[HttpPost("recognize")]`，归档的 `auto-detect` action 与 `AutoDetectRequest` 删除。
7. **权限码检查**：不要假定 `PermissionConventions` 有控制器分组映射。当前普通 POST 会落到 `api:smart-config:create`；若业务要沿用导入权限，应在 action 上显式加 `[AuditOperation("import", "document")]` 或在权限种子中给角色补 `api:smart-config:create` / 独立识别权限，并补授权测试。
8. DI 注册：`SmartConfigurationAppService` 注册在 `src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs`；当前 `ILlmEquivalenceAdjudicationService` 在 `src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs` 里通过 `LlmMatchingAssistService` 暴露，`ILlmStructureRecognitionService` 也必须在实际承载 LLM 依赖的 DI 文件中显式注册，避免 API 集成测试运行时解析失败。

- [ ] **Step 1: 写失败集成测试**——上传标准四列 docx（用测试项目现有 `CreateDocxBytes`/上传辅助方法，参照 `ExecutionHistoryApiTests`），调 `/api/smart-config/recognize`，断言 200、OverallDecision=autoApply、Tables 扁平返回、四字段列索引正确、Source=rule、FileId 为数字。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现编排 + 控制器**
- [ ] **Step 4: 全量测试，Commit**

```bash
git add src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService*.cs src/AcceptanceSpecSystem.Application/ServiceCollectionExtensions.cs src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs src/AcceptanceSpecSystem.Api/ServiceCollectionExtensions.cs tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeApiTests.cs
git commit -m "feat: 智能识别 recognize 端点输出全文档结构"
```

---

### Task 10: confirm 端点（模板沉淀 + 字典学习）

**Files:**
- Modify: `src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService.cs`（若超 500 行拆 `SmartConfigurationAppService.Learning.cs` partial）
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs`、`SmartConfigController.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigConfirmApiTests.cs`

**DTO 追加**：

```csharp
public class SmartConfirmRequest
{
    public int FileId { get; set; }
    public int CustomerId { get; set; }
    public List<SmartConfirmTableDto> Tables { get; set; } = new();
}

public class SmartConfirmTableDto
{
    /// <summary>扁平表格索引；Excel 表示工作表索引，Word 表示顶层表格索引。</summary>
    public int TableIndex { get; set; }
    public string TableName { get; set; } = string.Empty;
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

- 模板：按 `(CustomerId, Fingerprint)` upsert `DocumentTemplate`（存在→整套字段覆盖 + `UsageCount++`；不存在→新建，`TemplateName` = 客户名+日期，`UsageCount=1`），`LastUsedAt=UtcNow`。
- 字典：每条 Correction 且 `HeaderText` 非空白 → 查 `ColumnMappingRule` 是否已存在 `(Pattern=HeaderText, TargetField=对应枚举, CustomerId=本客户)`；无则插入 `{ MatchMode=Equals, Source=Learned, CustomerId, Priority=100, Enabled=true }`。
- 全局升级：插入后统计 `Source=Learned && Pattern && TargetField` 相同、`CustomerId` 互异的条数 ≥ 2 且不存在同 Pattern 的全局行（CustomerId==null）→ 追加一条 `CustomerId=null` 的全局 Learned 规则。
- 全过程 try/catch：沉淀失败记日志返回 `learned=false`，**不抛给调用方**。
- **观测日志**：confirm 成功后输出一行结构化日志（`LogInformation` + 命名占位符）：客户ID、每表决策（autoApply/needsConfirmation）、各字段来源（template/rule/llm）、修正字段列表——为后续识别效果分析留数据钩子，不建新表。

- [ ] **Step 1: 写失败集成测试**——① confirm 后同文件再 recognize，对应表 Decision=autoApply 且 Source=template（L0 命中）；② 带 Correction("project","管控要点",0) 的 confirm 后，DB 中存在 Learned 规则；③ 两个不同 customer 各 confirm 同词 → 出现全局行。
- [ ] **Step 2: 跑测试失败**
- [ ] **Step 3: 实现**
- [ ] **Step 4: 全量测试，Commit**

```bash
git add src/AcceptanceSpecSystem.Application/Services/SmartConfigurationAppService*.cs src/AcceptanceSpecSystem.Api/Controllers/SmartConfigController.cs src/AcceptanceSpecSystem.Api/DTOs/SmartConfigDtos.cs tests/AcceptanceSpecSystem.Api.Tests/SmartConfigConfirmApiTests.cs
git commit -m "feat: confirm 端点沉淀客户模板与自学习字典"
```

---

### Task 11: 集成夹具矩阵 + 降级护栏

**Files:**
- Test: `tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeMatrixTests.cs`

- [ ] **Step 1: 写四类夹具测试**（全部用测试内构造字节，xlsx 用 ClosedXML，docx 用 OpenXml，构造法参照 `ExecutionHistoryApiTests.CreateDocxBytes` 与 Excel 导入测试）：
  1. 怪表头 xlsx（“管控要点/判定基准/OK?NG/说明”）→ 200 且 needsConfirmation（无学习词、Fake LLM 返回 null 时的预期路径），PendingFields 非空；
  2. 多 Sheet xlsx（Sheet1 标准 → autoApply；Sheet2 怪表头 → needsConfirmation）→ 按表独立决策；
  3. 仅规格 docx（表头只有“规格/验收/备注”）→ MatchingMode=specificationOnly 且 autoApply；
  4. 空文档/无表格 → 200 + Tables 空 + OverallDecision=needsConfirmation（前端据此落高级模式），**不是 500**。
- [ ] **Step 2: 跑测试，修复实现直到全绿**
- [ ] **Step 3: 全量测试 + 架构边界测试确认，Commit**

```bash
git add tests/AcceptanceSpecSystem.Api.Tests/SmartConfigRecognizeMatrixTests.cs
git commit -m "test: 智能识别四类文档夹具矩阵与降级护栏"
```

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
  source: "template" | "rule" | "llm";
  reasoning: string;
}

export interface SmartTableResult {
  tableIndex: number;
  tableName: string;
  headers: string[];
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

export interface SmartRecognizeResponse {
  fileId: number;
  overallDecision: "autoApply" | "needsConfirmation";
  templateHit: { templateId: number; templateName: string; usageCount: number } | null;
  tables: SmartTableResult[];
}

export interface SmartCorrection {
  field: "project" | "specification" | "acceptance" | "remark";
  headerText: string;
  columnIndex: number;
}

export interface SmartConfirmTable {
  tableIndex: number;
  tableName: string;
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

export const recognizeDocument = (fileId: number, customerId?: number) =>
  http.request<{ code: number; data: SmartRecognizeResponse; message?: string }>(
    "post",
    "/api/smart-config/recognize",
    { data: { fileId, customerId } }
  );

export const confirmRecognition = (
  fileId: number,
  customerId: number,
  tables: SmartConfirmTable[]
) =>
  http.request<{ code: number; data: { learned: boolean }; message?: string }>(
    "post",
    "/api/smart-config/confirm",
    { data: { fileId, customerId, tables } }
  );
```

composable `useSmartRecognition`：状态机 `idle | recognizing | done | failed`，暴露 `recognize(fileId: number, customerId?: number)`、`confirm(...)`、`result`、`failed`（失败仅置状态供页面落高级模式，内部 catch 不上抛）。代码 ≤ 80 行，`http.request` 写法与 `web/src/api/document.ts` 现有函数保持一致（先读该文件对齐真实封装签名，若 `http.request` 泛型形参不同以现文件为准）。

- [ ] **Step 1: 写 API 封装与 composable**
- [ ] **Step 2: `cd web && pnpm typecheck` 通过**
- [ ] **Step 3: Commit**

```bash
git add web/src/api/smart-config.ts web/src/composables/useSmartRecognition.ts
git commit -m "feat: 前端智能识别 API 封装与共享 composable"
```

---

### Task 13: 摘要横幅 + 确认卡组件

**Files:**
- Create: `web/src/components/SmartRecognition/RecognitionSummaryBanner.vue`
- Create: `web/src/components/SmartRecognition/RecognitionConfirmCard.vue`

**Banner**（≤ 60 行）：props `result: SmartRecognizeResponse`；el-alert 样式一行摘要：“已自动识别 N 张表 · 来源：{模板名 | AI 识别}”，可展开列出每表映射 chips。表名显示取 `tableName || 表格 {tableIndex + 1}`。

**ConfirmCard**：props `tables`（仅含 needsConfirmation 表）、`fileId`；el-tabs 按表分签；每签内：
- 顶部若 `pendingFields.includes('project')` 或 `matchingMode==='specificationOnly'` 显示黄色提示条：“未识别到项目列，将按仅规格匹配——确认，或在下方指定项目列”。
- 主体复用 `TablePreview`（`web/src/views/data-import/components/TablePreview.vue`，传 `headerRowIndex/headerRowCount/dataStartRowIndex`）展示前 10 行；上方四个 el-select（项目/规格/验收/备注），选项优先来自 `SmartTableResult.headers`，若为空则监听 `TablePreview` 的 `loaded` 事件并用 `TableData.headers` 回填；选项格式为 `列N：表头文本`，初值取识别结果，`pendingFields` 对应的 select 加 `warning` 样式类；
- 底部主按钮「确认，继续」→ emit `confirm(tables: SmartConfirmTable[])`（含用户改动对应的 corrections：与初值不同的字段生成 `{field, headerText, columnIndex}`）；
- 右上角「手动配置」→ emit `manual`。

- [ ] **Step 1: 实现两组件**（选项/初值/corrections/headersJson 逻辑放组件内 `computed`+`ref`，勿引入新全局状态；`headersJson = JSON.stringify(headers)`）
- [ ] **Step 2: `pnpm typecheck` + `pnpm lint:eslint` 通过**
- [ ] **Step 3: Commit**

```bash
git add web/src/components/SmartRecognition/RecognitionSummaryBanner.vue web/src/components/SmartRecognition/RecognitionConfirmCard.vue
git commit -m "feat: 识别摘要横幅与确认卡组件"
```

---

### Task 14: 数据导入链路接入

**Files:**
- Modify: `web/src/views/data-import/index.vue`
- Modify: `web/src/views/data-import/composables/useDataImportPage.ts`（当前 5 步 `steps`、`canGoNext`、`goNext`、`watch(currentStep)` 都按索引写死，必须同步重排）
- Modify: `web/src/store/modules/dataImport.ts`（`currentStep` 与目标选择状态保留/重置规则）
- Modify: `web/src/views/data-import/dataImport.types.ts`、`dataImport.helpers.ts`、`composables/useDataImportBatchExecution.ts`（把识别结果转换为 `TableImportConfig`，并保留 Excel 1-based 绝对坐标）
- Modify: `web/src/views/data-import/components/DataImportStepUpload.vue`（承载上传 + 客户/制程/机型选择，或组合 `DataImportStepTarget.vue`）

**改造内容**：
1. 步骤数组由 5 步收敛为 3 步：上传/目标（同时选择客户/制程/机型）→ 确认/预览 → 完成。当前目标选择由 `DataImportStepTarget.vue` 在第 4 步加载，`useDataImportPage` 的 `watch(currentStep)` 也只在 step=3 加载客户/制程/机型；实施时必须改为页面初始化或上传/目标区加载，避免客户未加载导致无法识别。
2. 「选表」「列映射」两步的组件（`DataImportStepTableSelect.vue`、`DataImportStepMapping.vue`）不删文件，移入确认页「手动配置」抽屉（el-drawer）作为高级模式。现有 `FrontendViewBoundaryRefactorTests` 要求 `index.vue` 组合这些组件，若组件下沉到抽屉仍保留引用；若删除独立步骤引用，必须同步更新该边界测试。
3. 上传完成且已有客户 ID 后 → `useSmartRecognition.recognize(file.fileId, customerId)`；若用户还未选客户，先停在上传/目标选择合并区，不调用 recognize。加载态文案按 `source` 分层显示（“命中客户模板…”/“AI 分析表头中…”用 el-loading 文本）。
4. `overallDecision==='autoApply'` → 直接以识别结果填充既有导入预览参数（表索引/行范围/列索引），渲染 Banner + 预览；`needsConfirmation` → 渲染 ConfirmCard，`confirm` 事件回调里调 `confirmRecognition` 后进预览；`failed` 或 `tables` 为空 → ElMessage 提示并直接打开高级模式抽屉。
5. 「确认导入」沿用既有 `POST /api/documents/import` / `POST /api/documents/excel/import` 调用，参数来源换成识别/确认结果。Excel 必须执行上述 0-based 相对索引到 1-based 绝对坐标转换；Word 当前导入接口要求项目/规格/验收/备注四列齐全，Excel 当前导入接口要求项目/规格列齐全。识别结果不满足这些必填列时，不能直达导入，应转确认/高级模式或另行完成后端导入接口改造后再支持。

- [ ] **Step 1: 实现改造**
- [ ] **Step 2: `pnpm typecheck` 通过；启动前后端手工走一遍标准 xlsx 导入（上传→自动识别→预览→导入成功）**
- [ ] **Step 3: Commit**

```bash
git add web/src/views/data-import web/src/components/SmartRecognition web/src/composables/useSmartRecognition.ts web/src/api/smart-config.ts
git commit -m "feat: 数据导入收敛为智能识别两步流"
```

---

### Task 15: 智能填充链路接入

**Files:**
- Modify: `web/src/views/smart-fill/index.vue`、`SmartFillUploadStep.vue`、`SmartFillTableStep.vue`

**改造内容**：
1. 先处理 scope：当前客户/制程/机型选择在 `SmartFillMatchStep/MatchConfig`，上传完成时还拿不到 `customerId`。实施时必须把 scope 选择前置到上传/归属区，或上传后停在归属确认区，等 `customerId` 可用后再调用 `recognize(file.fileId, customerId)`；不得无客户直接识别后再声称 L0 客户模板生效。
2. recognize 完成后，AutoApply 表自动组装既有 `previewTables` 配置项（`BatchTableConfigItem`，字段对应：tableIndex/行范围/四列索引），**直接触发既有“开始匹配”动作**。
3. 仅规格模式注意：当前 `matchingMode` 在 `MatchConfigDto` / 前端 `MatchConfig` 上是请求级配置，`BatchTableConfig` 没有表级字段。若同一文档混合 `projectSpecification` 与 `specificationOnly`，必须拆成两次预览/执行请求，或先改造后端支持表级 MatchingMode；不得把“仅规格”伪塞进 `BatchTableConfigItem`。
4. 存在 needsConfirmation 表 → 表配置步只显示 ConfirmCard（AutoApply 表折叠进 Banner），确认后并入配置并开跑；
5. `BatchTableConfig.vue` 保留为「手动配置」抽屉（高级模式）；
6. 识别失败 → 落回现有 `SmartFillTableStep` 手动流程。

- [ ] **Step 1: 实现改造**
- [ ] **Step 2: `pnpm typecheck`；手工走一遍：标准文档上传→自动识别→自动开跑匹配→预览出结果；仅规格文档→匹配模式为仅规格**
- [ ] **Step 3: Commit**

```bash
git add web/src/views/smart-fill web/src/components/SmartRecognition web/src/composables/useSmartRecognition.ts web/src/api/smart-config.ts
git commit -m "feat: 智能填充收敛为智能识别两步流"
```

---

### Task 16: 回归 + 真实样本验收

- [ ] **Step 1: 后端全量** `dotnet test AcceptanceSpecSystem.sln -c Debug` 全绿（含架构边界测试）。
- [ ] **Step 2: 前端** `cd web && pnpm typecheck && pnpm test && pnpm build` 全绿。
- [ ] **Step 3: 真实样本手工验收**——用仓库根 `淮安庆鼎.xlsx`、`提供测试的文档/` 内样本各走一遍导入与填充：记录每份文档 直达/确认卡/高级模式 的落点与识别正确性；同客户第二次上传同结构文档必须直达（L0）。
- [ ] **Step 4: 结果达标**（老客户重复结构 100% 直达；新结构 ≥70% 免修正）**后 Commit 收尾**

```bash
git add docs tests web/src src
git commit -m "test: 智能识别真实样本回归验收"
```

---

## Self-Review 记录

- 规格覆盖：设计文档 §2 引擎三层+体检 → Task 4-8；§3 前端 → Task 12-15；§4 自学习 → Task 10；§5 API/降级/迁移/LLM预算 → Task 1-3, 9-11；§6 边界 → Task 8(数据范围/无表头走 L3)、Task 11(空文档)；§7 测试 → 各任务 TDD + Task 11/16；§9 观测/权限 → Task 9(权限)/10(日志)。仅规格场景 → Task 2(可空列)/8(双向判定)/11(夹具3)/15(模式下发)。无遗漏。
- 占位符：无 TBD/TODO；引用「镜像现有实现」处均给出确切镜像文件位置与差异点。
- 类型一致性：`SmartTableResultDto` ↔ `SmartTableResult`(TS) 字段一一对应；`RecognizeTableAsync` 签名在 Task 8 定义、Task 9 使用一致；`ColumnMappingRuleSource` 在 Task 3 定义、Task 9/10 使用一致。
