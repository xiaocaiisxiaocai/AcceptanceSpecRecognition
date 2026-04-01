# 匹配知识分组式维护改造 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将匹配知识配置从“别名映射/冲突词对”直出模型改为“实体组、单位组、字段组、左右冲突组”的分组式维护模型，并保持运行时匹配兼容。

**Architecture:** 后端新增分组作者视图 DTO，并在 `MatchingKnowledgeComposition` 中集中实现“分组视图 <-> 运行时展开模型”的双向转换与校验；前端页面切换为分组输入，只消费新的作者视图；运行时匹配引擎继续读取展开后的字典和冲突对，不直接感知分组作者模型。

**Tech Stack:** ASP.NET Core 8、C#、xUnit、FluentAssertions、Vue 3、TypeScript、Element Plus

---

### Task 1: 后端分组 DTO 与转换测试

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/DTOs/MatchingKnowledgeDtos.cs`
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeComposition.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigurationMatchingKnowledgeProviderTests.cs`

- [ ] **Step 1: 写失败测试，定义分组读取与保存契约**

为以下行为新增测试：
- `GET /api/matching-knowledge` 返回实体组、单位组、字段组、左右冲突组、单位换算
- `PUT /api/matching-knowledge` 接受分组视图并展开为运行时字典
- 同词归属两个分组时返回保存失败

- [ ] **Step 2: 运行相关测试，确认按预期失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~ConfigApisTests|FullyQualifiedName~ConfigurationMatchingKnowledgeProviderTests"`

Expected:
- 现有接口断言与新 DTO 结构不匹配
- 新增冲突校验测试失败

- [ ] **Step 3: 以最小改动实现分组 DTO 与转换逻辑**

实现要点：
- DTO 新增组模型，三类组用单列字符串集合，冲突组用左右集合
- `MatchingKnowledgeComposition` 新增聚合、展开、去重、冲突校验
- `ToDto` 改为返回分组作者视图
- `ToDomainModel` 继续输出运行时展开模型

- [ ] **Step 4: 重新运行相关测试，确认通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~ConfigApisTests|FullyQualifiedName~ConfigurationMatchingKnowledgeProviderTests"`

Expected: PASS

### Task 2: AI 草稿状态判定适配分组模型

**Files:**
- Modify: `src/AcceptanceSpecSystem.Api/Services/MatchingKnowledgeDraftGenerationService.cs`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/ConfigApisTests.cs`

- [ ] **Step 1: 写失败测试，覆盖草稿在分组模型下的重复与冲突判定**

新增测试覆盖：
- 草稿候选命中已有标准组时标记为重复
- 草稿候选与已有组归一值冲突时标记为冲突
- 冲突词草稿按左右组已展开结果判重

- [ ] **Step 2: 运行测试，确认失败原因是旧判定逻辑仍基于字典**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~ConfigApisTests"`

Expected: FAIL，提示状态判定仍沿用旧字典模型

- [ ] **Step 3: 实现最小适配逻辑**

实现要点：
- 将分组作者视图先展开为等价运行时字典/冲突对用于判重
- 维持现有草稿返回格式，避免额外牵连前端弹窗

- [ ] **Step 4: 重跑测试确认通过**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~ConfigApisTests"`

Expected: PASS

### Task 3: 前端分组页面与导入逻辑

**Files:**
- Modify: `web/src/api/matching-knowledge.ts`
- Modify: `web/src/views/config/matching-knowledge/index.vue`
- Test: `tests/AcceptanceSpecSystem.Api.Tests/MatchingKnowledgeFrontendRegressionTests.cs`

- [ ] **Step 1: 写失败测试，锁定新的页面文案和结构**

新增/调整断言覆盖：
- Tab 与卡片文案显示“实体组 / 单位组 / 字段组 / 冲突组”
- 单位别名与字段别名不再显示“别名 / 标准值”双列结构
- 冲突组显示“左冲突组 / 右冲突组”

- [ ] **Step 2: 运行测试，确认当前页面结构失败**

Run: `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~MatchingKnowledgeFrontendRegressionTests"`

Expected: FAIL，页面仍为旧双列表头

- [ ] **Step 3: 实现前端分组式页面**

实现要点：
- API 类型改为分组 DTO
- 页面三类别名改为单列分组输入
- 冲突词改为左右组输入
- 草稿导入按现有组或新组进行并入
- 提示文案明确“首项作为标准值”

- [ ] **Step 4: 运行前端回归测试与构建**

Run:
- `dotnet test tests/AcceptanceSpecSystem.Api.Tests/AcceptanceSpecSystem.Api.Tests.csproj -c Debug --filter "FullyQualifiedName~MatchingKnowledgeFrontendRegressionTests"`
- `pnpm build`

Expected:
- 页面回归测试 PASS
- 前端构建 PASS

### Task 4: 端到端回归验证

**Files:**
- Verify only

- [ ] **Step 1: 运行匹配知识相关后端测试全集**

Run: `dotnet test AcceptanceSpecSystem.sln -c Debug --filter "FullyQualifiedName~MatchingKnowledge|FullyQualifiedName~ConfigurationMatchingKnowledgeProviderTests|FullyQualifiedName~MatchEvidenceBuilderTests"`

Expected: PASS

- [ ] **Step 2: 对照 OpenSpec 核对需求**

核对：
- 实体组、单位组、字段组、左右冲突组是否全部落地
- 页面是否隐藏 `a -> a`、`功率 -> 功率` 这类实现细节
- 保存后运行时字典是否仍能驱动现有匹配

- [ ] **Step 3: 记录最终验证结果**

在交付说明中列出：
- 实际运行的测试命令
- 通过/失败情况
- 如有残留风险，明确指出
