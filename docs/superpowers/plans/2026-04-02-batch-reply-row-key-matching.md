# 批量回复按项目规格键匹配 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让批量回复按“项目 + 规格”唯一键匹配来源与目标行，不再要求两边行序一致，并在出现重复键时明确拒绝自动应用。

**Architecture:** 预检阶段为每张表建立标准化后的 `项目 + 规格` 唯一键索引，目标文件只要键集合一致即可通过；执行阶段基于“目标行号 -> 来源回复值”的映射写回，避免继续依赖来源行号。重复键冲突保持严格边界，直接返回人工处理提示。

**Tech Stack:** C#、ASP.NET Core、xUnit、FluentAssertions、OpenXML、ClosedXML

---

### Task 1: 补齐回归测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`

- [ ] **Step 1: 编写乱序匹配仍可预检通过并成功写回的失败测试**

```csharp
[Fact]
public async Task Execute_WhenTargetRowsReorderedButProjectAndSpecificationMatch_ShouldStillWriteBack()
{
    // 来源和目标拥有相同项目+规格，但顺序不同
    // 预检应通过，执行后应写回到目标对应行
}
```

- [ ] **Step 2: 运行单测确认当前实现失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests.Execute_WhenTargetRowsReorderedButProjectAndSpecificationMatch_ShouldStillWriteBack"`
Expected: FAIL，原因应为当前预检仍要求行顺序一致。

- [ ] **Step 3: 编写重复项目规格键时拒绝自动应用的失败测试**

```csharp
[Fact]
public async Task Preview_WhenSourceOrTargetContainsDuplicateProjectAndSpecification_ShouldReject()
{
    // 出现重复键时应返回人工处理提示
}
```

- [ ] **Step 4: 运行单测确认当前实现失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests.Preview_WhenSourceOrTargetContainsDuplicateProjectAndSpecification_ShouldReject"`
Expected: FAIL，原因应为当前实现尚未检测重复键。

### Task 2: 调整预检与执行映射

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplySessionService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`

- [ ] **Step 1: 为来源行快照增加标准化键字段或可派生键能力**

```csharp
internal sealed class BatchReplySourceRow
{
    public int RowIndex { get; set; }
    public string Project { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
}
```

- [ ] **Step 2: 预检按键匹配来源与目标，移除行号顺序绑定**

```csharp
var sourceLookup = BuildUniqueRowLookup(sourceTable.Rows);
var targetLookup = BuildUniqueRowLookup(targetRows);
```

- [ ] **Step 3: 生成“目标行号 -> 来源回复值”映射并用于写回**

```csharp
new BatchReplyWriteRow
{
    RowIndex = targetRow.RowIndex,
    Acceptance = sourceRow.Acceptance,
    Remark = sourceRow.Remark
}
```

- [ ] **Step 4: 对重复键返回明确错误提示**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests"`
Expected: PASS

### Task 3: 回归验证

**Files:**
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`

- [ ] **Step 1: 运行批量回复相关测试**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests"`
Expected: PASS

- [ ] **Step 2: 运行更大范围 API 回归**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug --filter "FullyQualifiedName~BatchReplyApiTests|FullyQualifiedName~ReviewRegressionTests|FullyQualifiedName~ArchitectureBoundaryTests"`
Expected: PASS
