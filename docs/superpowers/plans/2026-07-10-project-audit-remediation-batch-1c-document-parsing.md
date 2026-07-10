# Project Audit Remediation Batch 1C Document Parsing Errors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 区分合法空文档来源与真实解析失败，使损坏、I/O 和未知错误成为可诊断的业务错误。

**Architecture:** 保留 `DocumentTableAccessService` 的空集合契约用于解析器不可用、目标表不存在和合法无来源行；取消异常原样抛出，其余解析异常统一记录结构化日志并转换为现有 `ApplicationServiceException`。匹配来源和批量回复来源复用同一私有转换方法。

**Tech Stack:** ASP.NET Core 8、DocumentFormat.OpenXml、ClosedXML、xUnit、FluentAssertions、SQLite 测试宿主。

---

## 文件边界

**创建：**

- `tests/AcceptanceSpecSystem.Api.Tests/DocumentTableAccessFailureTests.cs`：合法空结果和损坏文件行为测试。
- `tests/AcceptanceSpecSystem.Api.Tests/DocumentParsingErrorGuardTests.cs`：两个来源提取路径的结构守卫。

**修改：**

- `src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs`：明确异常分类、结构化日志和业务错误转换。

## Task 1：建立损坏文档失败测试

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/DocumentTableAccessFailureTests.cs`

- [ ] **Step 1：编写损坏 Excel 测试**

测试通过 `ApiWebApplicationFactory.Services.CreateScope()` 解析 `DocumentTableAccessService`，构造只使用内存内容的文件：

```csharp
var wordFile = new WordFile
{
    Id = 91001,
    FileType = UploadedFileType.ExcelXlsx,
    FileContent = [0x01, 0x02, 0x03, 0x04]
};
```

调用 `ExtractMatchSourceItemsAsync(wordFile, 0, 0, 1)`，断言抛出 `ApplicationServiceException`，Code 为 400，消息为稳定用户文案“文档解析失败，请确认文件完整且未被占用”。

- [ ] **Step 2：编写合法空结果测试**

使用 ClosedXML 创建含一个空工作表的合法 xlsx 字节：

- 请求不存在的 table index，断言返回空集合。
- 请求存在但无可用来源行的空工作表，断言返回空集合；若解析器把完全空工作表表示为无表，只保留“目标表不存在”的空集合断言，不为测试改变解析器语义。

- [ ] **Step 3：运行行为测试确认红灯**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~DocumentTableAccessFailureTests
```

Expected: 损坏 Excel 用例 FAIL，当前实现捕获全部异常并返回空集合；合法空结果用例 PASS。

## Task 2：建立两个解析路径的结构失败守卫

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/DocumentParsingErrorGuardTests.cs`

- [ ] **Step 1：编写源码守卫**

读取 `DocumentTableAccessService.cs` 并断言：

- 不再包含无类型 `catch` 后直接 `return [];` 的解析分支。
- `ExtractMatchSourceItemsAsync` 和 `ExtractReplySourceItemsAsync` 均存在 `catch (OperationCanceledException)` 原样抛出。
- 两个方法均调用同一个 `CreateDocumentParsingException` 私有方法。
- 日志模板包含 `FileId`、`FileType`、`TableIndex`、`ExceptionType`，不包含 `FileName`、正文或单元格值占位符。

- [ ] **Step 2：运行守卫确认失败**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~DocumentParsingErrorGuardTests
```

Expected: FAIL，当前两个来源路径仍使用无类型 catch 返回空集合。

## Task 3：实现异常分类和结构化日志

**Files:**

- Modify: `src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs`

- [ ] **Step 1：注入 logger**

构造函数增加：

```csharp
ILogger<DocumentTableAccessService> logger
```

保存到 `_logger`；现有 `AddScoped<DocumentTableAccessService>()` 可由 DI 自动解析，不修改注册代码。

- [ ] **Step 2：将流打开纳入 try 边界**

在 `ExtractMatchSourceItemsAsync` 和 `ExtractReplySourceItemsAsync` 中，把 `OpenReadStream` 和解析器调用放入同一 try，确保文件不存在、权限和读取失败不会绕过转换逻辑。

- [ ] **Step 3：为两个方法应用一致 catch 顺序**

```csharp
catch (OperationCanceledException)
{
    throw;
}
catch (ArgumentOutOfRangeException)
{
    return [];
}
catch (NotSupportedException)
{
    return [];
}
catch (Exception ex)
{
    throw CreateDocumentParsingException(wordFile, tableIndex, ex);
}
```

目标表索引在可预检处继续先检查并返回空集合。不得把 `IOException`、`UnauthorizedAccessException`、Open XML/ClosedXML 损坏异常或未知异常放入降级集合。

- [ ] **Step 4：实现共享转换方法**

```csharp
private ApplicationServiceException CreateDocumentParsingException(
    WordFile wordFile,
    int tableIndex,
    Exception exception)
{
    _logger.LogError(
        exception,
        "文档解析失败: FileId={FileId}, FileType={FileType}, TableIndex={TableIndex}, ExceptionType={ExceptionType}",
        wordFile.Id,
        wordFile.FileType,
        tableIndex,
        exception.GetType().Name);

    return new ApplicationServiceException(
        400,
        "文档解析失败，请确认文件完整且未被占用");
}
```

日志不得增加文件名、路径、正文、表头或单元格内容。

- [ ] **Step 5：运行定向测试确认通过**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DocumentTableAccessFailureTests|FullyQualifiedName~DocumentParsingErrorGuardTests"
```

Expected: 全部 PASS。

- [ ] **Step 6：运行文档和匹配相关回归**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter "FullyQualifiedName~DocumentUploadTests|FullyQualifiedName~ExcelImport|FullyQualifiedName~BatchReply|FullyQualifiedName~MatchingPreview"
```

Expected: 0 失败；依赖真实外部资源的条件测试保持跳过。

- [ ] **Step 7：运行 Release 构建**

Run:

```powershell
dotnet build AcceptanceSpecSystem.sln -c Release --no-restore -p:TreatWarningsAsErrors=true
```

Expected: 0 警告、0 错误。

- [ ] **Step 8：提交 1C**

```powershell
git add src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs tests/AcceptanceSpecSystem.Api.Tests/DocumentTableAccessFailureTests.cs tests/AcceptanceSpecSystem.Api.Tests/DocumentParsingErrorGuardTests.cs docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1c-document-parsing.md
git commit -m "fix: 区分文档空结果与解析失败"
```
