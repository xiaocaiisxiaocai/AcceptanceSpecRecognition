# 智能填充严格复用 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为智能填充增加“应用到相同验规”的一次性严格复用能力，基于刚完成的填充结果批量回写相同模板文件，且不重新匹配、不调用 AI、不保存长期模板。

**Architecture:** 后端在现有填充任务快照中补充严格复用会话，保存来源文件类型、表格配置、整段数据区的项目/规格签名，以及已确认的验收/备注写回值。前端在智能填充完成态增加严格复用对话框，上传多个目标文件，先调预检接口显示逐文件结果，再对通过校验的文件执行批量写回并通过既有下载接口下载单文件或 zip 产物。

**Tech Stack:** ASP.NET Core、Entity Framework Core、Word/Excel 文档解析与写入器、Vue 3、Element Plus、OpenSpec

---

### Task 1: 后端严格复用回归测试

**Files:**
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/BatchFillTests.cs`

- [ ] **Step 1: 编写严格复用预检失败测试**

```csharp
[Fact]
public async Task StrictReusePreview_WhenProjectSpecificationOrderChanged_ShouldFail()
{
    // 先执行一次来源填充，再上传行顺序不同的目标文件，断言 preview 返回 canApply=false。
}
```

- [ ] **Step 2: 运行单测确认失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~StrictReusePreview_WhenProjectSpecificationOrderChanged_ShouldFail"`
Expected: FAIL，原因应为严格复用功能尚未实现或返回结果不符合预期。

- [ ] **Step 3: 编写严格复用执行成功测试**

```csharp
[Fact]
public async Task StrictReuseExecute_WithTwoSameDocx_ShouldReturnZipAndFilledFiles()
{
    // 来源文档执行填充后，对两个相同模板目标文件执行 strict reuse，
    // 断言 execute 成功且下载结果为 zip，zip 内两个文档都写入了来源的验收/备注。
}
```

- [ ] **Step 4: 运行单测确认失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~StrictReuseExecute_WithTwoSameDocx_ShouldReturnZipAndFilledFiles"`
Expected: FAIL，原因应为控制器内部类型或帮助方法缺失。

### Task 2: 后端严格复用实现

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Controllers/MatchingController.cs`

- [ ] **Step 1: 补齐严格复用会话模型**

实现 `StrictReuseSession`、`StrictReuseTableSnapshot`、`StrictReuseRowSignature`、`StrictReuseSourceTableDefinition`、`StrictReuseGeneratedFile`、下载产物模型，并给 `FillTaskResult` 增加严格复用与下载产物字段。

- [ ] **Step 2: 实现来源快照构建**

为单表/多表填充实现 `TryBuildStrictReuseSessionAsync(...)`，从来源文件读取完整数据区，保存表格配置、行签名以及本次确认后的写回值。

- [ ] **Step 3: 实现严格预检和执行**

实现 `ValidateStrictReuseTargetFileAsync(...)`、`GenerateStrictReuseTargetFileAsync(...)`、`SaveStrictReuseArtifactAsync(...)`，支持单文件直接下载、多文件 zip 下载。

- [ ] **Step 4: 清理过期产物**

调整 `SaveFillTaskSnapshotAsync(...)` 的过期清理逻辑，在删除旧任务前尝试清理 `DownloadArtifactRelativePath` 对应物理文件。

- [ ] **Step 5: 运行后端测试**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Release --filter "FullyQualifiedName~BatchFill|FullyQualifiedName~Fill"`
Expected: PASS。

### Task 3: 前端严格复用交互

**Files:**
- Modify: `web/src/api/matching.ts`
- Modify: `web/src/views/smart-fill/index.vue`
- Create: `web/src/views/smart-fill/components/StrictReuseDialog.vue`

- [ ] **Step 1: 扩展前端 API 类型**

为 `ExecuteFillRequest`、`BatchTableFillMapping` 增加严格复用所需列与行配置字段，并新增 `strictReusePreview`、`strictReuseExecute` 请求/响应类型与方法。

- [ ] **Step 2: 实现严格复用对话框**

对话框负责上传目标文件、调用预检接口、展示逐文件结果、过滤可执行文件并调用执行接口。

- [ ] **Step 3: 在智能填充完成态接入入口**

在 `web/src/views/smart-fill/index.vue` 中增加“应用到相同验规”按钮、弹窗状态管理和下载逻辑，文案明确“严格模式 / 不调用 AI / 不保存长期模板”。

- [ ] **Step 4: 运行前端类型校验**

Run: `pnpm --dir web typecheck`
Expected: PASS。

### Task 4: 规格与收尾

**Files:**
- Modify: `openspec/changes/add-smart-fill-strict-reuse/tasks.md`

- [ ] **Step 1: 运行 OpenSpec 校验**

Run: `openspec validate add-smart-fill-strict-reuse --strict`
Expected: PASS。

- [ ] **Step 2: 回填任务清单**

将 `openspec/changes/add-smart-fill-strict-reuse/tasks.md` 中已完成项改为 `- [x]`。
