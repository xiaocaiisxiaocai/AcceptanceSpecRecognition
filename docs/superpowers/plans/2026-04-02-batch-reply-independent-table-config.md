# 批量回复来源与目标逐表独立配置 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让批量回复同时支持来源文件与目标文件的逐表独立配置、目标表显式选择来源表，并改为按表预览、按文件执行。

**Architecture:** 后端把批量回复会话拆成来源配置和目标文件配置两层，预览阶段按“来源表配置 + 目标表配置”组合生成行级写回结果，执行阶段仅处理配置完整的目标文件。前端页面重构为多 Tab 工作流，分别承载来源配置、目标配置和执行结果，并在目标表级展示实时预览与失败原因。

**Tech Stack:** Vue 3、TypeScript、Element Plus、ASP.NET Core、xUnit、FluentAssertions、OpenXML、ClosedXML

---

### Task 1: 锁定新的 API 语义测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 编写“目标表可显式选择来源表”的失败测试**

```csharp
[Fact]
public async Task Preview_WhenTargetTableBindsDifferentSourceTable_ShouldUseSelectedSourceTable()
{
    // 目标表选择非同索引来源表时，预览应按选中的来源表计算
}
```

- [ ] **Step 2: 运行单测确认当前实现失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests.Preview_WhenTargetTableBindsDifferentSourceTable_ShouldUseSelectedSourceTable"`
Expected: FAIL，原因应为当前接口尚未支持目标表单独绑定来源表。

- [ ] **Step 3: 编写“执行只要求单个目标文件配置完整”的失败测试**

```csharp
[Fact]
public async Task Execute_WhenOneTargetFileCompleteAndAnotherIncomplete_ShouldExecuteOnlyCompleteFile()
{
    // 同批次下，只执行配置完整的目标文件
}
```

- [ ] **Step 4: 运行单测确认当前实现失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests.Execute_WhenOneTargetFileCompleteAndAnotherIncomplete_ShouldExecuteOnlyCompleteFile"`
Expected: FAIL，原因应为当前执行仍依赖旧的整批预检上下文。

### Task 2: 重构后端批量回复会话与 DTO

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/BatchReplyController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplySessionService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs`

- [ ] **Step 1: 先扩展请求/响应 DTO，表达来源配置与目标配置**

```csharp
public sealed class BatchReplyTargetTableConfig
{
    public int TargetTableIndex { get; set; }
    public int SourceTableIndex { get; set; }
}
```

- [ ] **Step 2: 运行相关测试，确认契约变更前仍失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests"`
Expected: 部分新增用例 FAIL。

- [ ] **Step 3: 用最小实现重构会话模型和预览编排**

```csharp
var sourceRows = await ExtractReplySourceItemsAsync(sourceFile, sourceConfig);
var targetRows = await ExtractMatchSourceItemsAsync(targetFile, ...targetConfig...);
```

- [ ] **Step 4: 运行批量回复相关测试确认通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj --filter "FullyQualifiedName~BatchReplyApiTests"`
Expected: PASS

### Task 3: 重构前端多 Tab 批量回复页面

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/batch-reply/index.vue`
- Modify: `web/src/views/smart-fill/components/BatchTableConfig.vue`

- [ ] **Step 1: 为前端 API 添加来源配置、目标配置和逐表预览模型的失败类型测试或静态契约调整**

```ts
export interface BatchReplyTargetTableConfig {
  targetTableIndex: number;
  sourceTableIndex: number;
}
```

- [ ] **Step 2: 先调整页面状态结构，拆出来源 Tab、目标文件 Tab、目标表 Tab**

```ts
const activeRootTab = ref("source");
const activeTargetFileId = ref("");
const activeTargetTableKey = ref("");
```

- [ ] **Step 3: 接入来源表选择、逐表预览和按文件执行按钮状态**

```ts
const canExecuteTargetFile = computed(() => ...);
```

- [ ] **Step 4: 运行前端构建确认通过**

Run: `pnpm --dir web build`
Expected: PASS

### Task 4: 回归与交付验证

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ExecutionHistoryApiTests.cs`

- [ ] **Step 1: 增加多 Tab 文案与来源表选择的回归断言**

```csharp
content.Should().Contain("来源配置");
content.Should().Contain("目标配置");
content.Should().Contain("来源表");
```

- [ ] **Step 2: 运行 OpenSpec 校验**

Run: `openspec validate refactor-batch-reply-independent-table-config --strict`
Expected: PASS

- [ ] **Step 3: 运行后端全量测试**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug`
Expected: PASS

- [ ] **Step 4: 运行前端构建**

Run: `pnpm --dir web build`
Expected: PASS
