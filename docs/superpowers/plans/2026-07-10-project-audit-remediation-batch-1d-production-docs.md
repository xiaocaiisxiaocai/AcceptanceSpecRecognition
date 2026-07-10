# Project Audit Remediation Batch 1D Production Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让 Docker、Windows Docker 和 IIS 部署文档与 Production 关闭 Swagger、使用健康检查验收的运行时行为一致，并记录批次 1 实施结果。

**Architecture:** 不修改 `Program.cs` 和 Production 配置，只用自动化守卫固定现有运行时决策，并删除部署文档中错误的 Swagger 验收入口。完成后更新审核报告中 P1-02、P1-03、P1-05、P1-06 的实施状态。

**Tech Stack:** Markdown、ASP.NET Core 8、xUnit、FluentAssertions、Git。

---

## 文件边界

**创建：**

- `tests/AcceptanceSpecSystem.Api.Tests/ProductionDeploymentDocumentationTests.cs`：Production Swagger/health 文档守卫。

**修改：**

- `docs/DEPLOY-DOCKER.md`：删除 Production Swagger 入口，只保留 `/health`。
- `docs/DEPLOY-IIS.md`：删除 `/api/swagger` 地址和验收步骤。
- `docs/DEPLOY-WINDOWS-DOCKER.md`：保持 health 验收并纳入守卫。
- `docs/项目深度审核与优化建议-2026-07-10.md`：追加批次 1 实施状态。
- 四份批次 1 实施计划：勾选已完成步骤。

## Task 1：建立 Production 部署文档失败守卫

**Files:**

- Create: `tests/AcceptanceSpecSystem.Api.Tests/ProductionDeploymentDocumentationTests.cs`

- [ ] **Step 1：编写失败测试**

沿用仓库根目录向上查找 helper，读取 `Program.cs` 和三份部署文档并断言：

```csharp
dockerDoc.Should().Contain("http://localhost:5290/health");
dockerDoc.Should().NotContain("/swagger");

iisDoc.Should().Contain("/api/health");
iisDoc.Should().NotContain("/api/swagger");

windowsDockerDoc.Should().Contain("/health");
windowsDockerDoc.Should().NotContain("/swagger");
```

对 `Program.cs` 额外断言 `UseSwagger()` 和 `UseSwaggerUI()` 仍位于 `app.Environment.IsDevelopment()` 条件块中，Production 没有独立 Swagger 开关。

- [ ] **Step 2：运行测试确认失败**

Run:

```powershell
dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~ProductionDeploymentDocumentationTests
```

Expected: FAIL，Docker 和 IIS 文档仍包含 Swagger 验收入口。

## Task 2：修正部署文档

**Files:**

- Modify: `docs/DEPLOY-DOCKER.md`
- Modify: `docs/DEPLOY-IIS.md`
- Modify: `docs/DEPLOY-WINDOWS-DOCKER.md`

- [ ] **Step 1：修正 Docker 文档**

删除 `Swagger：http://localhost/swagger`。API 验收仅保留 `http://localhost:5290/health`，并说明 Production 默认不提供 Swagger UI。

- [ ] **Step 2：修正 IIS 文档**

删除概览中的 `http://192.168.1.10/api/swagger` 和验收步骤“打开 `/api/swagger`”。保留 `/api/health` 返回 `healthy` 作为 API 启动判据，并保持前端访问验证为独立步骤。

- [ ] **Step 3：核对 Windows Docker 文档**

不新增 Swagger 文案；确认本机和局域网验收均指向 `/health`。只在需要统一措辞时做最小修改。

- [ ] **Step 4：运行文档守卫确认通过**

Run: Task 1 Step 2 命令。

Expected: PASS。

## Task 3：运行批次 1 全量验证

- [ ] **Step 1：运行前端测试和类型检查**

```powershell
pnpm --dir web test
pnpm --dir web typecheck
```

Expected: 全部 PASS。

- [ ] **Step 2：运行后端串行全量测试**

```powershell
dotnet test AcceptanceSpecSystem.sln -c Release --no-restore -m:1
```

Expected: 0 失败；真实 AI、真实 MySQL 等条件测试保持跳过。

- [ ] **Step 3：运行 warnings-as-errors Release 构建**

```powershell
dotnet build AcceptanceSpecSystem.sln -c Release --no-restore -p:TreatWarningsAsErrors=true
```

Expected: 0 警告、0 错误。

- [ ] **Step 4：检查提交范围**

```powershell
git status --short
git log -5 --oneline
git diff HEAD~3 HEAD --check
```

Expected: 仅剩 1D 文档和守卫改动未提交；1A、1B、1C 各自已有独立提交；diff check 无输出。

## Task 4：更新审核报告并提交批次状态

**Files:**

- Modify: `docs/项目深度审核与优化建议-2026-07-10.md`
- Modify: `docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1a-pagination.md`
- Modify: `docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1b-action-errors.md`
- Modify: `docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1c-document-parsing.md`
- Modify: `docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1d-production-docs.md`

- [ ] **Step 1：追加实施状态**

准确记录：

- P1-02：三个入口已使用共享全分页加载器，支持取消、去重、稳定顺序和页数保护。
- P1-03：已确认的删除/恢复/重置路径只忽略 Element Plus 主动取消，其他错误统一展示。
- P1-05：合法空结果继续降级，损坏/I/O/未知解析异常改为业务错误并记录脱敏结构化日志。
- P1-06：Production 继续关闭 Swagger，Docker/IIS 文档统一以 health 验收。

- [ ] **Step 2：勾选四份计划的完成项**

只勾选已经实际执行并验证的步骤；任何失败或未运行项保持未完成并在报告中说明。

- [ ] **Step 3：提交 1D 和批次状态**

```powershell
git add docs/DEPLOY-DOCKER.md docs/DEPLOY-IIS.md docs/DEPLOY-WINDOWS-DOCKER.md tests/AcceptanceSpecSystem.Api.Tests/ProductionDeploymentDocumentationTests.cs docs/项目深度审核与优化建议-2026-07-10.md docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1a-pagination.md docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1b-action-errors.md docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1c-document-parsing.md docs/superpowers/plans/2026-07-10-project-audit-remediation-batch-1d-production-docs.md
git commit -m "docs: 对齐生产部署验收并记录批次1结果"
```

- [ ] **Step 4：提交后最终检查**

```powershell
git status --short
git log -5 --oneline
git diff HEAD~1 HEAD --check
```

Expected: 工作树干净；最近提交按 1A、1B、1C、1D 主题分离；diff check 无输出。
