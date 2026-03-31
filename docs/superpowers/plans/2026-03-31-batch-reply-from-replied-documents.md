# 批量回复能力 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增独立“批量回复”能力，允许用户上传一份人工已回复的同模板文档，将其中的验收与备注批量应用到多个本地目标文件。

**Architecture:** 后端新增独立 `BatchReply` 用例链路，使用临时文件 + 临时会话模型承载来源文件与目标文件，复用现有表格提取、严格校验和写回基础设施。前端新增独立菜单、路由、页面和权限，不复用智能填充页面状态。

**Tech Stack:** ASP.NET Core 8 Web API、Vue 3 + TypeScript、Element Plus、xUnit、OpenXML、ClosedXML

---

### Task 1: 后端临时会话与写回模型

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Services/BatchReplySessionService.cs`
- Create: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingResultWriteBackService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Program.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`

- [ ] Step 1: 先写批量回复预检 API 测试，覆盖来源/目标同格式与结构一致时返回可应用结果
- [ ] Step 2: 跑该测试，确认因接口不存在或行为缺失而失败
- [ ] Step 3: 增加批量回复会话模型和最小会话服务，支持保存来源文件、目标文件和来源表格回复值
- [ ] Step 4: 在写回协作组件中增加“按来源回复值直接写回目标文件”的最小能力
- [ ] Step 5: 跑测试，确认预检路径通过

### Task 2: 独立 API 与严格校验

**Files:**
- Create: `src/AcceptanceSpecSystem.Api/Controllers/BatchReplyController.cs`
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/DocumentTableAccessService.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/BatchReplyAppService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`

- [ ] Step 1: 先写执行 API 测试，覆盖成功下载和格式不一致拒绝
- [ ] Step 2: 跑测试，确认失败点是执行接口/行为不存在
- [ ] Step 3: 新增 `preview / execute / download` 接口与 DTO，执行前再次复检
- [ ] Step 4: 抽出来源表格回复值提取逻辑，支持多表格和 Excel 起始行参数
- [ ] Step 5: 跑批量回复 API 测试，确认通过

### Task 3: 菜单、权限与前端页面

**Files:**
- Modify: `shared/navigation/navigation-manifest.json`
- Modify: `src/AcceptanceSpecSystem.Api/Services/AuthUserSeedService.cs`
- Create: `web/src/router/modules/batch-reply.ts`
- Modify: `web/src/api/matching.ts`
- Create: `web/src/views/batch-reply/index.vue`
- Modify: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] Step 1: 先写导航/权限回归测试，断言存在 `batch-reply` 菜单和独立权限码
- [ ] Step 2: 跑测试，确认因导航或种子权限缺失而失败
- [ ] Step 3: 补菜单清单、权限种子和前端 API 类型定义
- [ ] Step 4: 新增批量回复页面，提供来源上传、多表格配置、目标上传、预检与执行下载
- [ ] Step 5: 跑相关回归测试和前端构建，确认通过

### Task 4: 回归与规格同步

**Files:**
- Modify: `openspec/changes/add-batch-reply-from-replied-documents/tasks.md`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/BatchReplyApiTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ReviewRegressionTests.cs`

- [ ] Step 1: 跑 `openspec validate add-batch-reply-from-replied-documents --strict`
- [ ] Step 2: 跑 `dotnet test AcceptanceSpecSystem.sln -c Debug`
- [ ] Step 3: 跑 `pnpm build`
- [ ] Step 4: 将 OpenSpec 任务状态同步为已完成
